using System.ComponentModel.DataAnnotations;

namespace GroceryEasy.Infrastructure.Authentication;

/// <summary>
/// Access- and refresh-token settings, bound from the <c>Jwt</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Every property is annotated and the binding is registered with
/// <c>ValidateDataAnnotations().ValidateOnStart()</c>, so a missing or nonsense value <b>stops
/// the process at boot</b> rather than producing a confusing failure on the first login. The
/// legacy system read unvalidated environment variables and only discovered a missing one when a
/// token came out with an invalid expiry date (L-12).
/// </para>
/// <para>
/// Key names match <c>.env.example</c> exactly: <c>Jwt__Issuer</c>, <c>Jwt__Audience</c>,
/// <c>Jwt__SigningKey</c>, <c>Jwt__AccessTokenMinutes</c>, <c>Jwt__RefreshTokenDays</c>.
/// </para>
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Token issuer, validated on every incoming token.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Intended audience, validated on every incoming token.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// The symmetric signing key.
    /// </summary>
    /// <remarks>
    /// The 32-character floor is not arbitrary: HMAC-SHA256 wants a key at least as long as its
    /// 256-bit output, and a shorter one weakens the signature while still appearing to work.
    /// The development value in <c>.env.example</c> is deliberately labelled as replaceable.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters for HMAC-SHA256.")]
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Access-token lifetime in minutes. Fifteen by default.
    /// </summary>
    /// <remarks>
    /// Short on purpose. An access token is verified by signature alone and never checked against
    /// the database, which is what makes it fast and also what makes it impossible to revoke. The
    /// lifetime <em>is</em> the revocation window, so it is bounded to minutes and the ceiling
    /// here stops anyone "temporarily" raising it to a day.
    /// </remarks>
    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Refresh-token lifetime in days. Fourteen by default.</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;

    /// <summary>Access-token lifetime as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenMinutes);

    /// <summary>Refresh-token lifetime as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenDays);
}
