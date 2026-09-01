using System.ComponentModel.DataAnnotations;

namespace GroceryEasy.Infrastructure.Configuration;

/// <summary>
/// Where the single-page application lives. Every emailed link is built from this.
/// </summary>
/// <remarks>
/// <b>This exists specifically so that nothing ever builds a link from <c>Request.Host</c>.</b>
/// The legacy system built its password-reset link from <c>req.get("host")</c>, which is a
/// client-supplied header — so it was attacker-controllable, and because it resolved to the API
/// rather than the app, the reset link pointed at a route that only accepted PUT. Clicking it
/// issued a GET, fell through to the single-page-application catch-all, and returned the shell
/// page. Password reset never worked at all (L-09). A validated configuration value cannot fail
/// that way.
/// </remarks>
public sealed class FrontendOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Frontend";

    /// <summary>
    /// Absolute base address of the single-page application, for example
    /// <c>http://localhost:5173</c>. Bound from <c>Frontend__BaseUrl</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Builds an absolute link into the app, with query values escaped.</summary>
    /// <param name="path">Route path, for example <c>/verify-email</c>.</param>
    /// <param name="query">Query values, escaped by <see cref="Uri.EscapeDataString(string)"/>.</param>
    public string BuildLink(string path, IReadOnlyDictionary<string, string> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        string trimmedBase = BaseUrl.TrimEnd('/');
        string trimmedPath = path.StartsWith('/') ? path : '/' + path;

        string queryString = string.Join(
            '&',
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return queryString.Length == 0
            ? trimmedBase + trimmedPath
            : $"{trimmedBase}{trimmedPath}?{queryString}";
    }
}
