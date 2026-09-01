using FluentValidation;
using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Auth.Login;

/// <summary>Exchanges credentials for a token pair.</summary>
public sealed record LoginCommand(string Email, string Password) : ICommand<AuthenticationResult>;

/// <summary>Presence checks only — a login must not reveal what a valid email looks like.</summary>
internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // Deliberately no EmailAddress() rule. A malformed address would fail validation with
        // 400 while an unregistered but well-formed one fails with 401, and that difference is
        // a user-enumeration signal. Both take the same path to the same 401.
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}

/// <summary>
/// Validates credentials, then starts a fresh rotation lineage.
/// </summary>
/// <remarks>
/// The refresh token minted here begins a <b>new family</b>. Logging in from two devices produces
/// two independent families, so revoking one — by logging out, or because reuse was detected on
/// it — leaves the other alone. One family shared across devices would mean a single stolen token
/// logs the user out everywhere, every time.
/// </remarks>
internal sealed class LoginCommandHandler(
    IIdentityService identityService,
    ITokenService tokenService,
    ICurrentUser currentUser)
    : ICommandHandler<LoginCommand, AuthenticationResult>
{
    public async Task<Result<AuthenticationResult>> Handle(LoginCommand command, CancellationToken ct)
    {
        Result<AuthenticatedUser> validation =
            await identityService.ValidateCredentialsAsync(command.Email, command.Password, ct);

        if (validation.IsFailure)
        {
            return Result.Failure<AuthenticationResult>(validation.Error);
        }

        AuthenticatedUser user = validation.Value;

        AccessToken accessToken = tokenService.CreateAccessToken(user);

        IssuedRefreshToken refreshToken = await tokenService.IssueRefreshTokenAsync(
            user.Id,
            currentUser.IpAddress,
            currentUser.UserAgent,
            ct);

        return new AuthenticationResult(accessToken, refreshToken, user);
    }
}
