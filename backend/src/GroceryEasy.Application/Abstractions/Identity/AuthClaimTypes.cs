namespace GroceryEasy.Application.Abstractions.Identity;

/// <summary>
/// Claim names this system defines itself, beyond the registered JSON Web Token ones.
/// </summary>
/// <remarks>
/// A shared constant rather than the same string literal in the signer and the validator. Those
/// two must agree exactly, and a typo in one of them produces a token that validates as if the
/// claim were simply absent — which fails open, silently, on the one check that exists to catch
/// deleted and changed accounts.
/// </remarks>
public static class AuthClaimTypes
{
    /// <summary>
    /// Identity's security stamp, carried in every access token and re-checked per request.
    /// </summary>
    /// <remarks>
    /// The stamp changes whenever something security-relevant about an account changes — a
    /// deletion, a role change, a password reset — so comparing the claim against the stored
    /// value gives a stateless token a revocation story. This is the mechanism behind the fix
    /// for L-07.
    /// </remarks>
    public const string SecurityStamp = "security_stamp";
}
