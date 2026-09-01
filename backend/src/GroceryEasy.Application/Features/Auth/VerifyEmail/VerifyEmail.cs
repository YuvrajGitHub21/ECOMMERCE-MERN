using FluentValidation;
using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Auth.VerifyEmail;

/// <summary>Confirms an email address using the token from the verification link.</summary>
/// <param name="Email">The address being confirmed.</param>
/// <param name="Token">The token minted at registration.</param>
public sealed record VerifyEmailCommand(string Email, string Token) : ICommand<Unit>;

internal sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256);

        // No length ceiling beyond the sane: Identity's token format is its own business and
        // pinning an exact length here would break the first time the provider changes.
        RuleFor(c => c.Token).NotEmpty().MaximumLength(1024);
    }
}

/// <summary>
/// Confirms the address, or returns one indistinguishable failure.
/// </summary>
/// <remarks>
/// A bad token, a token for a different address, an expired token and an already-used token all
/// return <see cref="AuthErrors.EmailConfirmationFailed"/>. The user's next action is the same in
/// every case — request a new link — and telling an unauthenticated caller which addresses have
/// pending verifications is an enumeration oracle of the kind recorded as L-11.
/// </remarks>
internal sealed class VerifyEmailCommandHandler(IIdentityService identityService)
    : ICommandHandler<VerifyEmailCommand, Unit>
{
    public async Task<Result<Unit>> Handle(VerifyEmailCommand command, CancellationToken ct)
    {
        Result confirmation = await identityService.ConfirmEmailAsync(command.Email, command.Token, ct);

        return confirmation.IsFailure
            ? Result.Failure<Unit>(confirmation.Error)
            : Result.Success(Unit.Value);
    }
}
