using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Application.Features.Auth;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Users.GetCurrentUser;

/// <summary>Returns the signed-in user's profile.</summary>
public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>;

/// <summary>The caller's own profile.</summary>
/// <param name="Id">User identifier.</param>
/// <param name="Email">Email address.</param>
/// <param name="FullName">Display name.</param>
/// <param name="EmailConfirmed">Whether the address has been verified. An unverified user may browse but not order.</param>
/// <param name="Roles">Role names, for rendering navigation only — never for an access decision.</param>
public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string FullName,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles);

/// <summary>
/// Reads the caller's identity from the token, then re-reads the account from the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>The second read is the point, and it is what closes L-07.</b> A valid signature proves the
/// token was issued by this server; it does not prove the account still exists. The legacy system
/// trusted the token alone, so a deleted user's request reached a handler that dereferenced a
/// null user and returned 500 — an authentication problem surfacing as a server crash.
/// </para>
/// <para>
/// Here a missing account is <see cref="AuthErrors.UserNoLongerExists"/>, which the error mapper
/// turns into <b>401, not 500</b>. The access token's security-stamp claim catches the same case
/// one layer earlier for most requests; this is the belt to that pair of braces, and it is cheap.
/// </para>
/// </remarks>
internal sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IIdentityService identityService)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        if (currentUser.Id is not Guid userId)
        {
            return Result.Failure<CurrentUserResponse>(AuthErrors.NotAuthenticated);
        }

        AuthenticatedUser? user = await identityService.FindByIdAsync(userId, ct);

        return user is null
            ? Result.Failure<CurrentUserResponse>(AuthErrors.UserNoLongerExists)
            : Result.Success(new CurrentUserResponse(
                user.Id,
                user.Email,
                user.FullName,
                user.EmailConfirmed,
                user.Roles));
    }
}
