using System.Security.Cryptography;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Infrastructure.Identity;

/// <summary>
/// One issued refresh token, stored as a hash and belonging to a rotation lineage.
/// </summary>
/// <remarks>
/// <para>
/// <b>The raw token is never stored.</b> Only <see cref="TokenHash"/>, a SHA-256 of the random
/// bytes, ever reaches the database. A dump of this table therefore grants nobody a session,
/// which is the same reasoning that makes storing a password hash rather than a password
/// obvious — refresh tokens are bearer credentials with a fourteen-day life and deserve the
/// same treatment.
/// </para>
/// <para>
/// <b>Why a plain SHA-256 and not a slow hash like bcrypt.</b> A password is low-entropy and
/// guessable, so the hash must be deliberately expensive. This token is 256 bits from a
/// cryptographic random source, so brute-forcing it is not on the table and a slow hash would
/// only add latency to every refresh. Fast hash, high-entropy input.
/// </para>
/// <para>
/// <b><see cref="FamilyId"/> is what makes reuse detection possible.</b> Every token minted by
/// rotating an existing one inherits its family. Presenting a token that has already been
/// rotated means a copy leaked, and the correct response is to kill the whole lineage rather
/// than the single token — the attacker and the victim are both holding tokens from it.
/// </para>
/// </remarks>
public sealed class RefreshToken : Entity, IAuditableEntity
{
    /// <summary>How many random bytes back each token. 256 bits.</summary>
    public const int TokenSizeInBytes = 32;

    private RefreshToken()
    {
        // Entity Framework materialisation only.
    }

    private RefreshToken(
        Guid userId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset expiresAt,
        string? createdByIp,
        string? userAgent)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
        UserAgent = userAgent;
    }

    /// <summary>The user this token authenticates.</summary>
    public Guid UserId { get; private set; }

    /// <summary>SHA-256 of the raw token, base64. Unique across the table.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Groups every token descended from one login.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>When this token stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Set the moment the token is rotated, revoked, or killed by reuse detection.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The token minted when this one was rotated. Null until then.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Caller address at issue time, for the security log.</summary>
    public string? CreatedByIp { get; private set; }

    /// <summary>Caller user agent at issue time, for the security log.</summary>
    public string? UserAgent { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Live means not revoked and not expired. Both conditions, always checked together.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>
    /// Mints a token at the head of a brand-new family. This is a fresh login, not a rotation.
    /// </summary>
    /// <returns>The entity to persist, and the raw token to hand the caller exactly once.</returns>
    public static (RefreshToken Token, string RawToken) Issue(
        Guid userId,
        DateTimeOffset now,
        TimeSpan lifetime,
        string? createdByIp,
        string? userAgent)
    {
        string rawToken = GenerateRawToken();

        RefreshToken token = new(
            userId,
            Hash(rawToken),
            Guid.CreateVersion7(),
            now.Add(lifetime),
            createdByIp,
            userAgent);

        return (token, rawToken);
    }

    /// <summary>
    /// Rotates this token: revokes it, mints a successor in the <b>same</b> family, and links the
    /// two so the lineage can be walked in either direction.
    /// </summary>
    /// <remarks>
    /// Called only after the presented token has been confirmed active. The caller runs this and
    /// the persistence of both rows inside one transaction, so a crash between the two cannot
    /// leave a revoked token with no successor — which would log the user out for no reason.
    /// </remarks>
    public (RefreshToken Token, string RawToken) Rotate(
        DateTimeOffset now,
        TimeSpan lifetime,
        string? createdByIp,
        string? userAgent)
    {
        string rawToken = GenerateRawToken();

        RefreshToken successor = new(
            UserId,
            Hash(rawToken),
            FamilyId,
            now.Add(lifetime),
            createdByIp,
            userAgent);

        RevokedAt = now;
        ReplacedByTokenId = successor.Id;

        return (successor, rawToken);
    }

    /// <summary>
    /// Revokes this token if it is still live. Idempotent, so revoking a whole family does not
    /// need to filter out the ones already dead.
    /// </summary>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }

    /// <summary>
    /// SHA-256, base64. Deterministic and unsalted on purpose: the lookup is by hash, so a salt
    /// would make it impossible to find the row at all.
    /// </summary>
    public static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

    private static string GenerateRawToken() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenSizeInBytes));

    /// <summary>
    /// Base64url, so the value survives a cookie, a header and a query string without escaping.
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
