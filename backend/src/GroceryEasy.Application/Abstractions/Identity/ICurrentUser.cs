namespace GroceryEasy.Application.Abstractions.Identity;

/// <summary>
/// Who is making the current request.
/// </summary>
/// <remarks>
/// <para>
/// Reading the caller's identity from an injected service rather than from an <c>HttpContext</c>
/// reachable inside a handler keeps Application testable and keeps handlers from depending on
/// there being an HTTP request at all — which stops being true the moment a background job runs
/// the same handler, in Phase 5.
/// </para>
/// <para>
/// <see cref="Id"/> is null for an anonymous caller. Handlers must treat that as an expected
/// state and return an <c>Unauthorized</c> failure, never dereference it — the legacy system's
/// equivalent assumption is what turned a deleted user's request into a 500 (L-07).
/// </para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>The authenticated user's identifier, or null when anonymous.</summary>
    Guid? Id { get; }

    /// <summary>The caller's IP address, for the refresh-token security log.</summary>
    string? IpAddress { get; }

    /// <summary>The caller's user agent, for the refresh-token security log.</summary>
    string? UserAgent { get; }
}
