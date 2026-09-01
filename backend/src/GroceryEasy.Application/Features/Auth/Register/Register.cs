using FluentValidation;
using GroceryEasy.Application.Abstractions.Email;
using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Auth.Register;

/// <summary>Creates an account and sends the verification email.</summary>
/// <param name="Email">The address to register. Compared case-insensitively — the column is <c>citext</c>.</param>
/// <param name="Password">Plain text on the wire and never logged. Hashed by Identity, never by us.</param>
/// <param name="FullName">Display name.</param>
public sealed record RegisterCommand(string Email, string Password, string FullName)
    : ICommand<Unit>;

/// <summary>
/// Input shape only. Whether the email is already taken is a question for the database, and
/// asking it here would be a check that is stale by the time the insert runs.
/// </summary>
internal sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        // Length only. Composition rules (a digit, a symbol, a capital) are Identity's job and
        // are configured in one place there; duplicating them here means two sources of truth
        // that disagree the first time either is tuned.
        RuleFor(c => c.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);

        RuleFor(c => c.FullName)
            .NotEmpty()
            .MaximumLength(200);
    }
}

/// <summary>
/// Registers the user, then emails a verification link.
/// </summary>
/// <remarks>
/// <para>
/// Returns <see cref="Unit"/> rather than the new user's identifier. Registration is not a login
/// — the caller gets no token and no session, because the address is unverified. Handing back an
/// identifier would only invite a client to use it for something.
/// </para>
/// <para>
/// The email send sits inside the command's transaction, which is a knowing compromise: if the
/// mail server is down the whole registration rolls back and the customer can retry, rather than
/// ending up with an account they can never verify. The correct answer is the transactional
/// outbox, and it arrives in Phase 4 — at which point this handler raises a domain event instead
/// and the send moves out of the request entirely.
/// </para>
/// </remarks>
internal sealed class RegisterCommandHandler(
    IIdentityService identityService,
    IAuthNotificationService notifications)
    : ICommandHandler<RegisterCommand, Unit>
{
    public async Task<Result<Unit>> Handle(RegisterCommand command, CancellationToken ct)
    {
        Result<Guid> registration = await identityService.RegisterAsync(
            command.Email,
            command.Password,
            command.FullName,
            ct);

        if (registration.IsFailure)
        {
            return Result.Failure<Unit>(registration.Error);
        }

        Result<string> token = await identityService.GenerateEmailConfirmationTokenAsync(
            registration.Value,
            ct);

        if (token.IsFailure)
        {
            return Result.Failure<Unit>(token.Error);
        }

        await notifications.SendEmailVerificationAsync(
            command.Email,
            command.FullName,
            token.Value,
            ct);

        return Result.Success(Unit.Value);
    }
}
