using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Auth.Logout;

/// <summary>
/// Ends the session by revoking the presented token's whole family.
/// </summary>
/// <remarks>
/// <b>This is a POST, never a GET.</b> A logout reachable by GET is triggered by any image tag,
/// prefetch or link a third-party page can put in front of the user — a cross-site request
/// forgery whose payload is "sign this person out". The legacy application's logout was a GET
/// (L-10). Being a command rather than a query is what makes that structural here: queries do
/// not write, and the endpoint that maps a command is a POST.
/// </remarks>
/// <param name="PresentedToken">The raw token from the cookie. Null is fine — see the handler.</param>
public sealed record LogoutCommand(string? PresentedToken) : ICommand<Unit>;

/// <summary>
/// Revokes the family and always succeeds.
/// </summary>
/// <remarks>
/// <b>Logout is idempotent and never fails.</b> Logging out twice, logging out with no cookie, or
/// logging out with a token that was already revoked all return success, because in every one of
/// those cases the caller's desired end state — no session — already holds. Returning an error
/// would leave a client stuck in a loop trying to log out of a session that is already gone, and
/// would leak whether the presented token was real.
/// </remarks>
internal sealed class LogoutCommandHandler(ITokenService tokenService)
    : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Result<Unit>> Handle(LogoutCommand command, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(command.PresentedToken))
        {
            await tokenService.RevokeFamilyAsync(command.PresentedToken, ct);
        }

        return Result.Success(Unit.Value);
    }
}
