using System.Net;
using GroceryEasy.Application.Abstractions.Email;
using GroceryEasy.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GroceryEasy.Infrastructure.Email;

/// <summary>
/// Builds the two account emails and their links.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both links are built from the validated <c>Frontend:BaseUrl</c> setting.</b> Nothing here
/// can see the incoming request, which is the structural reason it cannot repeat L-09 — the
/// legacy reset link was built from the client-supplied <c>Host</c> header and pointed at the
/// API rather than the app, so it never worked.
/// </para>
/// <para>
/// Templates are string interpolation rather than a templating engine. Two messages do not
/// justify the dependency; if a third and a fourth arrive, this is where Razor templating or
/// MJML would go.
/// </para>
/// <para>
/// Every interpolated value passes through <see cref="WebUtility.HtmlEncode(string?)"/>. The name
/// comes from user input at registration, and an unencoded name containing markup would be
/// injected into the message body of an email the recipient trusts.
/// </para>
/// </remarks>
internal sealed class AuthNotificationService(
    IEmailSender emailSender,
    IOptions<FrontendOptions> frontendOptions)
    : IAuthNotificationService
{
    private readonly FrontendOptions _frontend = frontendOptions.Value;

    public Task SendEmailVerificationAsync(
        string toEmail,
        string fullName,
        string token,
        CancellationToken cancellationToken)
    {
        string link = _frontend.BuildLink(
            "/verify-email",
            new Dictionary<string, string>
            {
                ["email"] = toEmail,
                ["token"] = token,
            });

        string body = $"""
            <p>Hello {WebUtility.HtmlEncode(fullName)},</p>
            <p>Welcome to GroceryEasy. Please confirm your email address to finish setting up your account.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Confirm my email address</a></p>
            <p>If you did not create this account, you can ignore this message.</p>
            """;

        return emailSender.SendAsync(toEmail, "Confirm your GroceryEasy account", body, cancellationToken);
    }

    public Task SendPasswordResetAsync(
        string toEmail,
        string fullName,
        string token,
        CancellationToken cancellationToken)
    {
        string link = _frontend.BuildLink(
            "/reset-password",
            new Dictionary<string, string>
            {
                ["email"] = toEmail,
                ["token"] = token,
            });

        string body = $"""
            <p>Hello {WebUtility.HtmlEncode(fullName)},</p>
            <p>We received a request to reset your GroceryEasy password.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Choose a new password</a></p>
            <p>If you did not ask for this, no action is needed — your password has not changed.</p>
            """;

        return emailSender.SendAsync(toEmail, "Reset your GroceryEasy password", body, cancellationToken);
    }
}
