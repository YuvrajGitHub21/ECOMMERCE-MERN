using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Abstractions.Identity;

/// <summary>
/// Everything Application needs from the credential store, expressed without naming it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this interface exists at all.</b> ASP.NET Core Identity's <c>UserManager&lt;TUser&gt;</c>
/// is generic over a user type that derives from a NuGet base class, so it cannot cross into
/// Application without dragging the whole package with it and breaking the architecture test
/// that keeps Application free of Infrastructure. This interface is the seam: Application
/// orchestrates, Infrastructure knows it is Identity underneath.
/// </para>
/// <para>
/// <b>It is deliberately narrow and it returns plain types.</b> Not <c>IdentityResult</c>, not
/// <c>ApplicationUser</c> — <see cref="Result"/> and small records. The moment a framework type
/// appears in this signature the seam has leaked and the abstraction is only pretending.
/// </para>
/// <para>
/// <b>What it does not do:</b> issue tokens. That is <see cref="ITokenService"/>, because the
/// token layer is hand-built and is where this project's actual security engineering lives.
/// </para>
/// </remarks>
public interface IIdentityService
{
    /// <summary>Creates a user with the Customer role. The password is hashed by Identity.</summary>
    /// <returns>The new user's identifier, or a Conflict when the email is taken.</returns>
    Task<Result<Guid>> RegisterAsync(
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken);

    /// <summary>Mints an email-confirmation token for the user.</summary>
    Task<Result<string>> GenerateEmailConfirmationTokenAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Confirms an email address against a previously issued token.</summary>
    Task<Result> ConfirmEmailAsync(string email, string token, CancellationToken cancellationToken);

    /// <summary>
    /// Checks an email and password pair.
    /// </summary>
    /// <remarks>
    /// Returns the same <c>Auth.InvalidCredentials</c> failure for an unknown email, a wrong
    /// password, and an unconfirmed account. Distinguishing them tells an attacker which
    /// addresses are registered, which is the enumeration weakness recorded as L-11.
    /// </remarks>
    Task<Result<AuthenticatedUser>> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    /// <summary>Looks a user up by identifier. Null when absent or deleted.</summary>
    Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Looks a user up by email. Null when absent.</summary>
    Task<AuthenticatedUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Mints a password-reset token.</summary>
    Task<Result<string>> GeneratePasswordResetTokenAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Resets a password against a previously issued token.</summary>
    Task<Result> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken);
}

/// <summary>
/// A user, as Application sees one.
/// </summary>
/// <param name="Id">The user's identifier.</param>
/// <param name="Email">The confirmed or unconfirmed email address.</param>
/// <param name="FullName">Display name.</param>
/// <param name="EmailConfirmed">Whether the address has been verified.</param>
/// <param name="Roles">Role names held by this user.</param>
/// <param name="SecurityStamp">
/// Identity's marker for "something security-relevant about this account changed". It is
/// embedded in the access token and re-checked on every authenticated request, so deleting a
/// user or changing their roles invalidates tokens already issued. In the legacy system a
/// deleted user's token stayed valid and then crashed the request with a null dereference
/// (L-07).
/// </param>
public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    string FullName,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    string SecurityStamp);
