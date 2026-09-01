using FluentValidation;
using GroceryEasy.Application.Abstractions.Email;
using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Auth.ForgotPassword;

/// <summary>Starts a password reset.</summary>
public sealed record ForgotPasswordCommand(string Email) : ICommand<Unit>;

internal sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256);
    }
}

/// <summary>
/// Sends a reset link if the account exists, and reports success either way.
/// </summary>
/// <remarks>
/// <para>
/// <b>This handler returns success for an address that is not registered.</b> That is the entire
/// design, not an oversight. An endpoint that answers "no such user" is a free membership oracle:
/// anyone can test an address list against it and learn who has an account. The legacy
/// forgot-password endpoint did exactly that (L-11).
/// </para>
/// <para>
/// The integration test for this asserts the two responses are byte-identical, not merely both
/// 200 — a differing body or a differing header is the same leak in a quieter form. Response
/// timing differs slightly (a real account does hashing work an absent one does not); closing
/// that would mean moving the send off the request path, which the Phase 4 outbox does anyway.
/// </para>
/// </remarks>
internal sealed class ForgotPasswordCommandHandler(
    IIdentityService identityService,
    IAuthNotificationService notifications)
    : ICommandHandler<ForgotPasswordCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ForgotPasswordCommand command, CancellationToken ct)
    {
        AuthenticatedUser? user = await identityService.FindByEmailAsync(command.Email, ct);

        // No account, or an account whose address was never verified. Silently do nothing —
        // and specifically do not tell the caller which of the two it was, or that it was either.
        if (user is null || !user.EmailConfirmed)
        {
            return Result.Success(Unit.Value);
        }

        Result<string> token = await identityService.GeneratePasswordResetTokenAsync(user.Id, ct);

        if (token.IsSuccess)
        {
            await notifications.SendPasswordResetAsync(user.Email, user.FullName, token.Value, ct);
        }

        return Result.Success(Unit.Value);
    }
}
