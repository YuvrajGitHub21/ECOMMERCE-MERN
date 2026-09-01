using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Features.Auth;

/// <summary>
/// Every failure the auth slices can return, named once.
/// </summary>
/// <remarks>
/// <para>
/// Codes are stable and machine-readable (<c>Auth.InvalidCredentials</c>), because they reach the
/// client inside the RFC 9457 ProblemDetails body and a frontend that wants to special-case one
/// of them needs something better than matching on English prose.
/// </para>
/// <para>
/// <b><see cref="InvalidCredentials"/> is used for three different situations</b> — unknown
/// email, wrong password, unconfirmed account — and that is deliberate. Telling the caller which
/// one it was hands an attacker a user-enumeration oracle. The legacy forgot-password endpoint
/// answered differently for known and unknown addresses and was exactly that oracle (L-11).
/// </para>
/// </remarks>
public static class AuthErrors
{
    /// <summary>The email is already registered.</summary>
    public static readonly Error EmailAlreadyInUse = Error.Conflict(
        "Auth.EmailAlreadyInUse",
        "An account with this email address already exists.");

    /// <summary>
    /// Login failed. Identical for unknown email, wrong password and unconfirmed account.
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "Auth.InvalidCredentials",
        "The email address or password is incorrect.");

    /// <summary>The refresh cookie was absent, unknown, expired, or already rotated.</summary>
    /// <remarks>
    /// One error for all four, for the same reason as <see cref="InvalidCredentials"/>: the
    /// client can do nothing differently in any of them except log in again, and distinguishing
    /// them tells whoever is holding a stolen token how far they got.
    /// </remarks>
    public static readonly Error InvalidRefreshToken = Error.Unauthorized(
        "Auth.InvalidRefreshToken",
        "The session is no longer valid. Please sign in again.");

    /// <summary>Email confirmation failed: bad token, wrong address, or already used.</summary>
    public static readonly Error EmailConfirmationFailed = Error.Validation(
        "Auth.EmailConfirmationFailed",
        "This verification link is invalid or has already been used.");

    /// <summary>Password reset failed: bad or expired token.</summary>
    public static readonly Error PasswordResetFailed = Error.Validation(
        "Auth.PasswordResetFailed",
        "This password reset link is invalid or has expired.");

    /// <summary>The request needs an authenticated caller and did not have one.</summary>
    public static readonly Error NotAuthenticated = Error.Unauthorized(
        "Auth.NotAuthenticated",
        "Authentication is required.");

    /// <summary>The token authenticated, but the account behind it no longer exists.</summary>
    /// <remarks>
    /// 401 and not 500. This is the direct answer to L-07, where a deleted user's still-valid
    /// token reached a handler that assumed the user existed and dereferenced null.
    /// </remarks>
    public static readonly Error UserNoLongerExists = Error.Unauthorized(
        "Auth.UserNoLongerExists",
        "This account is no longer available.");

    /// <summary>Identity rejected the registration — a weak password, usually.</summary>
    public static Error RegistrationFailed(string description) =>
        Error.Validation("Auth.RegistrationFailed", description);
}
