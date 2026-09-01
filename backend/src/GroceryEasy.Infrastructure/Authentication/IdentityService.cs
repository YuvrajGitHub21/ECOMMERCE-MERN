using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Features.Auth;
using GroceryEasy.Domain.Common;
using GroceryEasy.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace GroceryEasy.Infrastructure.Authentication;

/// <summary>
/// The ASP.NET Core Identity side of <see cref="IIdentityService"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class is the only place in the system that touches <see cref="UserManager{TUser}"/>.
/// Everything above it deals in <see cref="Result"/> and <see cref="AuthenticatedUser"/>, so the
/// choice of credential store stays swappable and Application stays free of the package.
/// </para>
/// <para>
/// <b>Password hashing is entirely Identity's.</b> It ships a vetted PBKDF2 implementation with a
/// sane iteration count, a per-user salt, and a versioned format that can be upgraded in place.
/// Hand-rolling it is a week spent arriving somewhere worse — and the legacy system's attempt
/// re-hashed the stored hash on every save because a pre-save hook was missing a guard, which
/// silently broke password reset for every user who ever updated their profile (L-08).
/// </para>
/// </remarks>
internal sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
    public async Task<Result<Guid>> RegisterAsync(
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Checked explicitly so the caller gets a 409 with a clear code. The unique index on the
        // normalised email is still the actual guarantee — this check races, and losing the race
        // surfaces below as a CreateAsync failure rather than as a duplicate row.
        ApplicationUser? existing = await userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            return Result.Failure<Guid>(AuthErrors.EmailAlreadyInUse);
        }

        ApplicationUser user = new()
        {
            UserName = email,
            Email = email,
            FullName = fullName,
        };

        IdentityResult creation = await userManager.CreateAsync(user, password);

        if (!creation.Succeeded)
        {
            return Result.Failure<Guid>(DescribeFailure(creation));
        }

        IdentityResult roleAssignment = await userManager.AddToRoleAsync(user, Roles.Customer);

        return roleAssignment.Succeeded
            ? Result.Success(user.Id)
            : Result.Failure<Guid>(DescribeFailure(roleAssignment));
    }

    public async Task<Result<string>> GenerateEmailConfirmationTokenAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return Result.Failure<string>(AuthErrors.UserNoLongerExists);
        }

        return Result.Success(await userManager.GenerateEmailConfirmationTokenAsync(user));
    }

    public async Task<Result> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByEmailAsync(email);

        // Unknown address and bad token give the same answer, so this endpoint cannot be used to
        // discover which addresses are registered.
        if (user is null)
        {
            return Result.Failure(AuthErrors.EmailConfirmationFailed);
        }

        IdentityResult confirmation = await userManager.ConfirmEmailAsync(user, token);

        return confirmation.Succeeded
            ? Result.Success()
            : Result.Failure(AuthErrors.EmailConfirmationFailed);
    }

    public async Task<Result<AuthenticatedUser>> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            // Hash a throwaway password anyway. Returning immediately makes an unknown address
            // measurably faster to reject than a known one with a wrong password, and that timing
            // difference is a user-enumeration oracle in its own right. This keeps the two paths
            // doing comparable work.
            _ = userManager.PasswordHasher.HashPassword(new ApplicationUser(), password);
            return Result.Failure<AuthenticatedUser>(AuthErrors.InvalidCredentials);
        }

        // Honours lockout, so repeated failures eventually stop being answered at all.
        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return Result.Failure<AuthenticatedUser>(AuthErrors.InvalidCredentials);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return Result.Failure<AuthenticatedUser>(AuthErrors.InvalidCredentials);
        }

        // An unverified address is the same failure as a wrong password, for the same reason.
        if (!user.EmailConfirmed)
        {
            return Result.Failure<AuthenticatedUser>(AuthErrors.InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        return Result.Success(await ToAuthenticatedUserAsync(user));
    }

    public async Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());

        return user is null ? null : await ToAuthenticatedUserAsync(user);
    }

    public async Task<AuthenticatedUser?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByEmailAsync(email);

        return user is null ? null : await ToAuthenticatedUserAsync(user);
    }

    public async Task<Result<string>> GeneratePasswordResetTokenAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return Result.Failure<string>(AuthErrors.UserNoLongerExists);
        }

        return Result.Success(await userManager.GeneratePasswordResetTokenAsync(user));
    }

    public async Task<Result> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            return Result.Failure(AuthErrors.PasswordResetFailed);
        }

        IdentityResult reset = await userManager.ResetPasswordAsync(user, token, newPassword);

        // Identity rotates the security stamp on a successful reset, and because that stamp is a
        // claim in every access token and is re-validated per request, every token already issued
        // to this account stops working. That is the correct behaviour for a password change and
        // it comes for free.
        return reset.Succeeded
            ? Result.Success()
            : Result.Failure(AuthErrors.PasswordResetFailed);
    }

    private async Task<AuthenticatedUser> ToAuthenticatedUserAsync(ApplicationUser user)
    {
        IList<string> roles = await userManager.GetRolesAsync(user);

        return new AuthenticatedUser(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.EmailConfirmed,
            [.. roles],
            user.SecurityStamp ?? string.Empty);
    }

    /// <summary>
    /// Turns Identity's error list into one validation error.
    /// </summary>
    /// <remarks>
    /// Identity's messages are already user-facing and describe password-policy failures
    /// accurately, so they are passed through rather than replaced with something vaguer.
    /// </remarks>
    private static Error DescribeFailure(IdentityResult result) =>
        AuthErrors.RegistrationFailed(
            string.Join(' ', result.Errors.Select(error => error.Description)));
}
