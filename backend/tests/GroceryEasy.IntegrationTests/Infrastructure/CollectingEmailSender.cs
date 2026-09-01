using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using GroceryEasy.Application.Abstractions.Email;

namespace GroceryEasy.IntegrationTests.Infrastructure;

/// <summary>
/// Captures sent mail in memory instead of talking to a mail server.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes the verification and password-reset flows testable end to end. The token is
/// generated inside the application and only ever leaves it through a link in an email, so a test
/// that wants to complete a registration has to read it out of the message — exactly as a user
/// would, and exercising the real link-building code on the way.
/// </para>
/// <para>
/// Reading the token from the message rather than reaching into <c>UserManager</c> for one also
/// means the test would catch a link built from the wrong base address, which is precisely the
/// legacy failure L-09.
/// </para>
/// </remarks>
public sealed partial class CollectingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    /// <summary>Everything sent since the last reset, oldest first.</summary>
    public IReadOnlyCollection<SentEmail> Sent => [.. _sent];

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        _sent.Enqueue(new SentEmail(toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }

    /// <summary>Drops everything captured. Called between tests.</summary>
    public void Clear() => _sent.Clear();

    /// <summary>
    /// The most recent message to an address, or null.
    /// </summary>
    public SentEmail? LastTo(string email) =>
        _sent.Where(message =>
                string.Equals(message.To, email, StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();

    /// <summary>
    /// Pulls a query-string value out of the first link in a message body.
    /// </summary>
    /// <remarks>
    /// The href is HTML-encoded, so <c>&amp;</c> separates the parameters and has to be decoded
    /// before parsing — which is correct for an HTML attribute and is a real step a mail client
    /// performs too.
    /// </remarks>
    public static string ExtractLinkParameter(string htmlBody, string parameterName)
    {
        Match href = HrefPattern().Match(htmlBody);

        if (!href.Success)
        {
            throw new InvalidOperationException("The message contains no link.");
        }

        Uri link = new(WebUtility.HtmlDecode(href.Groups[1].Value));

        string? value = System.Web.HttpUtility.ParseQueryString(link.Query)[parameterName];

        return value
            ?? throw new InvalidOperationException(
                $"The link has no '{parameterName}' parameter: {link}");
    }

    [GeneratedRegex("href=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern();
}

/// <summary>One captured message.</summary>
/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">Body, containing the link and its token.</param>
public sealed record SentEmail(string To, string Subject, string HtmlBody);
