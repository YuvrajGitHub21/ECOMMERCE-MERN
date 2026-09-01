using System.Security.Claims;
using GroceryEasy.Application.Abstractions.Identity;

namespace GroceryEasy.Api.Authentication;

/// <summary>
/// Reads the caller's identity from the current HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the API project rather than Infrastructure because it is purely an HTTP concern
/// — it reads claims, a socket address and a header. Putting it in Infrastructure would have
/// meant that project taking a dependency on <c>IHttpContextAccessor</c> to serve a request
/// context it otherwise has no interest in.
/// </para>
/// <para>
/// <see cref="Id"/> is null for anonymous callers, and handlers are expected to treat that as an
/// ordinary branch rather than an impossibility.
/// </para>
/// </remarks>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? Id
    {
        get
        {
            string? subject = httpContextAccessor.HttpContext?.User
                .FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(subject, out Guid id) ? id : null;
        }
    }

    /// <summary>
    /// The caller's address, for the refresh-token security log.
    /// </summary>
    /// <remarks>
    /// <c>RemoteIpAddress</c>, deliberately — not <c>X-Forwarded-For</c>. That header is
    /// client-supplied and trivially spoofed, so trusting it unconditionally would let an
    /// attacker write whatever they liked into the security log for a reuse-detection event. When
    /// this runs behind a known proxy in Phase 6, the correct fix is
    /// <c>ForwardedHeadersMiddleware</c> with the proxy explicitly whitelisted, which populates
    /// <c>RemoteIpAddress</c> safely and leaves this code unchanged.
    /// </remarks>
    public string? IpAddress =>
        httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            string? value = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

            // Truncated to the column width. A header is caller-controlled and can be enormous,
            // and an insert that throws on overflow would turn a cosmetic detail into a failed
            // login.
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value[..Math.Min(value.Length, 512)];
        }
    }
}
