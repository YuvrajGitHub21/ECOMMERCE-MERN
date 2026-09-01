using GroceryEasy.Domain.Common;

namespace GroceryEasy.Application.Abstractions.Identity;

/// <summary>
/// Issues, rotates and revokes the two tokens this system uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two tokens with opposite properties, on purpose.</b> The access token is a JSON Web Token
/// with a fifteen-minute life, verified by signature alone so no request touches the database to
/// authenticate. That speed is bought by giving up revocation, which is why it is short-lived.
/// The refresh token is the opposite: opaque, long-lived, stored hashed, and checked against the
/// database on every use — so it *can* be revoked, which is what makes reuse detection possible.
/// </para>
/// <para>
/// <b>Delivery differs too, and for a reason.</b> The access token goes in the response body and
/// the browser keeps it in memory only — never <c>localStorage</c>, which any cross-site
/// scripting flaw can read. The refresh token goes in an <c>HttpOnly</c> cookie scoped to
/// <c>/api/auth</c>, which script cannot read at all and which is not attached to ordinary API
/// calls. Neither storage location is safe for both tokens, which is why there are two (L-10).
/// </para>
/// </remarks>
public interface ITokenService
{
    /// <summary>Signs an access token for the user. No database write.</summary>
    AccessToken CreateAccessToken(AuthenticatedUser user);

    /// <summary>
    /// Starts a new rotation lineage. Called on successful login, never on refresh.
    /// </summary>
    Task<IssuedRefreshToken> IssueRefreshTokenAsync(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates and rotates a presented refresh token.
    /// </summary>
    /// <remarks>
    /// <para>The whole security argument of this phase lives in the implementation:</para>
    /// <list type="number">
    ///   <item>Unknown hash gives 401.</item>
    ///   <item>Already revoked means the token leaked — <b>revoke the entire family</b>, log a
    ///     security warning, and give 401. An honest client never presents a rotated token,
    ///     so a presentation of one means two parties hold tokens from this lineage.</item>
    ///   <item>Expired gives 401.</item>
    ///   <item>Otherwise revoke the presented token, mint a successor in the same family, link
    ///     them, and return a fresh pair.</item>
    /// </list>
    /// <para>All of it inside one transaction, or a crash mid-rotation leaves a user with a
    /// revoked token and no replacement.</para>
    /// </remarks>
    Task<Result<TokenPair>> RotateRefreshTokenAsync(
        string presentedToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the family the presented token belongs to. This is logout: it ends every session
    /// descended from that login, not merely the current request's token.
    /// </summary>
    Task RevokeFamilyAsync(string presentedToken, CancellationToken cancellationToken);
}

/// <summary>A signed access token and the moment it stops being valid.</summary>
/// <param name="Value">The compact JSON Web Token.</param>
/// <param name="ExpiresAt">Expiry, so the client can refresh ahead of a failure rather than after one.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>The pair handed back by a rotation, plus the user it belongs to.</summary>
/// <remarks>
/// <see cref="User"/> is included because a rotation has to load the user anyway — signing the
/// new access token needs their roles and security stamp — so returning it saves the caller a
/// second round trip to fetch what was just read. It is also the freshest possible copy: a role
/// changed a minute ago is reflected on the next refresh rather than at the end of the refresh
/// token's fourteen-day window.
/// </remarks>
/// <param name="AccessToken">Goes in the response body.</param>
/// <param name="RefreshToken">Goes in the <c>HttpOnly</c> cookie, never the body.</param>
/// <param name="User">The user the rotated lineage belongs to, re-read during rotation.</param>
public sealed record TokenPair(AccessToken AccessToken, IssuedRefreshToken RefreshToken, AuthenticatedUser User);

/// <summary>An opaque refresh token and the moment it stops being accepted.</summary>
/// <remarks>
/// The expiry travels with the value so the endpoint can set the cookie's lifetime from the
/// token itself. Hard-coding a duration at the cookie-writing site means two places decide how
/// long a session lasts, and they drift the first time either is tuned — leaving a browser
/// presenting a cookie the server has already stopped accepting, or discarding one it would have.
/// </remarks>
/// <param name="Value">The raw token. Handed to the caller exactly once.</param>
/// <param name="ExpiresAt">When the server will stop accepting it.</param>
public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt);
