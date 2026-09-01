using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Auth.Refresh;

/// <summary>
/// Rotates a refresh token into a new pair.
/// </summary>
/// <remarks>
/// The token arrives from the cookie, read by the endpoint. This is a command rather than a query
/// because it writes: the presented token is revoked and a successor inserted, and both must land
/// in one transaction — which the pipeline's transaction behaviour gives commands and
/// deliberately does not give queries.
/// </remarks>
/// <param name="PresentedToken">The raw token from the <c>HttpOnly</c> cookie.</param>
public sealed record RefreshCommand(string? PresentedToken) : ICommand<AuthenticationResult>;

/// <summary>
/// Delegates the rotation to <see cref="ITokenService"/>, which owns the reuse-detection rules.
/// </summary>
/// <remarks>
/// There is deliberately no validator. An absent or malformed cookie is not a 400 — it is a 401,
/// identical to an unknown, expired or already-rotated token. All four mean the same thing to the
/// client (sign in again), and distinguishing them tells whoever is holding a stolen token how
/// far they got.
/// </remarks>
internal sealed class RefreshCommandHandler(
    ITokenService tokenService,
    ICurrentUser currentUser)
    : ICommandHandler<RefreshCommand, AuthenticationResult>
{
    public async Task<Result<AuthenticationResult>> Handle(RefreshCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.PresentedToken))
        {
            return Result.Failure<AuthenticationResult>(AuthErrors.InvalidRefreshToken);
        }

        Result<TokenPair> rotation = await tokenService.RotateRefreshTokenAsync(
            command.PresentedToken,
            currentUser.IpAddress,
            currentUser.UserAgent,
            ct);

        return rotation.IsFailure
            ? Result.Failure<AuthenticationResult>(rotation.Error)
            : Result.Success(new AuthenticationResult(
                rotation.Value.AccessToken,
                rotation.Value.RefreshToken,
                rotation.Value.User));
    }
}
