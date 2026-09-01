using FluentValidation;
using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Auth.ResetPassword;

/// <summary>Completes a password reset using the token from the emailed link.</summary>
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword)
    : ICommand<Unit>;

internal sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256);
        RuleFor(c => c.Token).NotEmpty().MaximumLength(1024);
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

/// <summary>
/// Resets the password.
/// </summary>
/// <remarks>
/// <para>
/// Identity rotates the user's security stamp as part of a successful password reset. Because the
/// stamp is a claim in every access token and is re-checked on every authenticated request, that
/// single side effect <b>invalidates every access token already issued to this account</b> — which
/// is exactly what should happen when a password changes, and it costs nothing extra here.
/// </para>
/// <para>
/// Existing refresh-token families are not revoked by this handler. Doing so is arguably correct
/// and is a deliberate deferral rather than an omission: it needs a "revoke every family for a
/// user" path that Phase 1 has no other caller for, and the fifteen-minute access-token window
/// bounds the exposure meanwhile. Recorded in the implementation log.
/// </para>
/// </remarks>
internal sealed class ResetPasswordCommandHandler(IIdentityService identityService)
    : ICommandHandler<ResetPasswordCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ResetPasswordCommand command, CancellationToken ct)
    {
        Result reset = await identityService.ResetPasswordAsync(
            command.Email,
            command.Token,
            command.NewPassword,
            ct);

        return reset.IsFailure
            ? Result.Failure<Unit>(reset.Error)
            : Result.Success(Unit.Value);
    }
}
