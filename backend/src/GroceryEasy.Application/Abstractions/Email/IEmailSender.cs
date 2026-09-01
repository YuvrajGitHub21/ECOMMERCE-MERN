namespace GroceryEasy.Application.Abstractions.Email;

/// <summary>
/// Sends transactional mail. One method, because that is all Phase 1 needs.
/// </summary>
/// <remarks>
/// <para>
/// Behind this sit MailKit against Mailpit in development, and a collecting implementation in
/// tests that records messages in memory so a test can assert "a verification link was sent to
/// this address" and then read the token out of it without a mail server anywhere.
/// </para>
/// <para>
/// <b>Every link in every message is built from the validated <c>Frontend:BaseUrl</c> setting,
/// never from the incoming request's <c>Host</c> header.</b> The legacy system built its
/// password-reset link from <c>req.get("host")</c>, which is attacker-controllable and — because
/// it pointed at the API rather than the single-page application — meant password reset never
/// worked at all (L-09).
/// </para>
/// </remarks>
public interface IEmailSender
{
    /// <summary>Sends one message. Throws on transport failure; the outbox arrives in Phase 4.</summary>
    Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken);
}
