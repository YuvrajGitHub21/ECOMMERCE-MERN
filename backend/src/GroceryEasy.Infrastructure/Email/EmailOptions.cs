using System.ComponentModel.DataAnnotations;

namespace GroceryEasy.Infrastructure.Email;

/// <summary>
/// Outbound mail settings, bound from the <c>Email</c> section.
/// </summary>
/// <remarks>
/// In development these point at Mailpit on <c>localhost:1025</c>, which accepts anything and
/// shows it in a web inbox on port 8025 — so nothing ever leaves the machine, and a developer can
/// read the verification link without a real mailbox. Mailpit is in the <c>full</c> Compose
/// profile.
/// </remarks>
public sealed class EmailOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Email";

    /// <summary>SMTP host. Bound from <c>Email__Host</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Host { get; init; } = string.Empty;

    /// <summary>SMTP port. Bound from <c>Email__Port</c>.</summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 1025;

    /// <summary>Envelope sender address.</summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>Display name on the sender.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromName { get; init; } = string.Empty;

    /// <summary>Optional SMTP username. Empty for Mailpit, which accepts any credentials.</summary>
    public string? UserName { get; init; }

    /// <summary>Optional SMTP password.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// Whether to negotiate STARTTLS. Off for Mailpit, on for any real provider.
    /// </summary>
    /// <remarks>
    /// A setting rather than a hard-coded value because the development catcher speaks plain SMTP
    /// and a production relay must not. Defaulting it to <see langword="false"/> is the safe
    /// direction here: the failure mode is a refused connection in production, which is loud,
    /// rather than a silent plaintext send.
    /// </remarks>
    public bool UseStartTls { get; init; }
}
