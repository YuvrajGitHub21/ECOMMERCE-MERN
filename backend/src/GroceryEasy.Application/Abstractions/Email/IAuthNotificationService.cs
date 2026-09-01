namespace GroceryEasy.Application.Abstractions.Email;

/// <summary>
/// Sends the two account emails, given only a token and a recipient.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this sits above <see cref="IEmailSender"/>.</b> Building the link needs the frontend's
/// base address, and writing the message needs a template. Neither is Application's business:
/// one is deployment configuration, the other is presentation. Handing a handler the raw sender
/// plus a base address would put URL construction into Application, where it would be duplicated
/// per email and would eventually be built from something convenient and wrong — which is
/// exactly how the legacy system ended up building reset links from the request's <c>Host</c>
/// header (L-09).
/// </para>
/// <para>
/// So Application says "verify this address, here is the token" and Infrastructure decides what
/// that looks like and where it points.
/// </para>
/// </remarks>
public interface IAuthNotificationService
{
    /// <summary>Sends the email-verification link.</summary>
    Task SendEmailVerificationAsync(
        string toEmail,
        string fullName,
        string token,
        CancellationToken cancellationToken);

    /// <summary>Sends the password-reset link.</summary>
    Task SendPasswordResetAsync(
        string toEmail,
        string fullName,
        string token,
        CancellationToken cancellationToken);
}
