using GroceryEasy.Application.Abstractions.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace GroceryEasy.Infrastructure.Email;

/// <summary>
/// Sends mail over SMTP with MailKit.
/// </summary>
/// <remarks>
/// MailKit rather than <c>System.Net.Mail.SmtpClient</c>, which Microsoft's own documentation
/// marks as not recommended for new development and which handles modern TLS negotiation poorly.
/// In development this talks to Mailpit, so nothing leaves the machine.
/// </remarks>
internal sealed partial class MailKitEmailSender(
    IOptions<EmailOptions> options,
    ILogger<MailKitEmailSender> logger)
    : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        MimeMessage message = new();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart(TextFormat.Html) { Text = htmlBody };

        using SmtpClient client = new();

        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            await client.AuthenticateAsync(
                _options.UserName,
                _options.Password ?? string.Empty,
                cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        LogSent(subject, toEmail);
    }

    /// <summary>
    /// Recipient and subject only — never the body.
    /// </summary>
    /// <remarks>
    /// The body carries a verification or password-reset token, and a token written to a log is a
    /// token in whatever aggregates that log. Source-generated rather than a plain
    /// <c>LogInformation</c> call so the arguments are not boxed when the level is disabled.
    /// </remarks>
    [LoggerMessage(Level = LogLevel.Information, Message = "Sent '{Subject}' to {Recipient}.")]
    private partial void LogSent(string subject, string recipient);
}
