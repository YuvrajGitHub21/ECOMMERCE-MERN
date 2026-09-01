namespace GroceryEasy.Api.Authentication;

/// <summary>
/// The one place the refresh cookie is written, read and cleared.
/// </summary>
/// <remarks>
/// <para>
/// Every attribute below is load-bearing, and the legacy cookie had none of them (L-10):
/// </para>
/// <list type="bullet">
///   <item><b><c>HttpOnly</c></b> — page script cannot read it, so a cross-site scripting flaw
///     cannot steal the long-lived credential. This is the entire reason the refresh token is a
///     cookie and the access token is not.</item>
///   <item><b><c>Secure</c></b> — never sent over plain HTTP.</item>
///   <item><b><c>SameSite=Strict</c></b> — not attached to requests originating from another
///     site, which is what stops a third-party page from silently refreshing a session.</item>
///   <item><b><c>Path=/api/auth</c></b> — attached only to the four endpoints that need it,
///     instead of riding along on every API call the application ever makes. Scoping it to
///     <c>/</c> would put the credential on the wire hundreds of times a session for no reason.</item>
/// </list>
/// <para>
/// <c>Secure</c> is relaxed in Development only, because the local single-page application runs on
/// plain <c>http://localhost</c> and a <c>Secure</c> cookie would simply never be stored — which
/// looks exactly like a broken login and costs an afternoon to diagnose.
/// </para>
/// </remarks>
internal static class RefreshTokenCookie
{
    /// <summary>The cookie name.</summary>
    public const string Name = "ge_refresh";

    /// <summary>The path the cookie is scoped to. Must match the auth endpoint group's prefix.</summary>
    public const string Path = "/api/auth";

    /// <summary>Writes the cookie.</summary>
    public static void Write(HttpContext context, string refreshToken, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Append(Name, refreshToken, Options(context, expiresAt));
    }

    /// <summary>Reads the presented token, or null when the cookie is absent.</summary>
    public static string? Read(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Request.Cookies.TryGetValue(Name, out string? token)
            && !string.IsNullOrWhiteSpace(token)
                ? token
                : null;
    }

    /// <summary>
    /// Clears the cookie.
    /// </summary>
    /// <remarks>
    /// The delete must carry the same <c>Path</c> and <c>SameSite</c> the cookie was written
    /// with, or the browser treats it as a different cookie and silently keeps the original —
    /// a logout that appears to work and does not.
    /// </remarks>
    public static void Clear(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Delete(Name, Options(context, DateTimeOffset.UnixEpoch));
    }

    private static CookieOptions Options(HttpContext context, DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = !context.RequestServices
            .GetRequiredService<IHostEnvironment>()
            .IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true,
    };
}
