using System.Security.Claims;
using System.Text;
using GroceryEasy.Application.Abstractions.Identity;
using GroceryEasy.Application.Features.Auth;
using GroceryEasy.Domain.Common;
using GroceryEasy.Infrastructure.Identity;
using GroceryEasy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GroceryEasy.Infrastructure.Authentication;

/// <summary>
/// Signs access tokens, and issues, rotates and revokes refresh tokens.
/// </summary>
/// <remarks>
/// This is the hand-built part of authentication and the part worth reading. Identity supplies
/// the password hasher and the user store; the token lifecycle below is this project's own.
/// </remarks>
internal sealed class TokenService(
    ApplicationDbContext dbContext,
    IIdentityService identityService,
    IServiceScopeFactory scopeFactory,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    ILogger<TokenService> logger)
    : ITokenService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public AccessToken CreateAccessToken(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset issuedAt = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = issuedAt.Add(_jwt.AccessTokenLifetime);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(AuthClaimTypes.SecurityStamp, user.SecurityStamp),
        ];

        // One claim per role rather than a comma-joined string, so the standard role-claim
        // machinery and every RequireRole policy work without a custom parser.
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        SigningCredentials credentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        SecurityTokenDescriptor descriptor = new()
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = credentials,
        };

        string token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new AccessToken(token, expiresAt);
    }

    public async Task<IssuedRefreshToken> IssueRefreshTokenAsync(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        (RefreshToken token, string rawToken) = RefreshToken.Issue(
            userId,
            timeProvider.GetUtcNow(),
            _jwt.RefreshTokenLifetime,
            ipAddress,
            userAgent);

        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(rawToken, token.ExpiresAt);
    }

    /// <summary>
    /// The rotation and reuse-detection routine. Read the numbered comments in order.
    /// </summary>
    public async Task<Result<TokenPair>> RotateRefreshTokenAsync(
        string presentedToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string presentedHash = RefreshToken.Hash(presentedToken);

        // 1. Look the presented token up by hash. The raw value is never stored, so this is the
        //    only way to find it — and the unique index makes it a single index seek.
        RefreshToken? stored = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(token => token.TokenHash == presentedHash, cancellationToken);

        // 2. Unknown. Either forged or from a family that has already been purged.
        if (stored is null)
        {
            logger.LogWarning(
                "Refresh rejected: unknown token presented from {IpAddress}.",
                ipAddress);

            return Result.Failure<TokenPair>(AuthErrors.InvalidRefreshToken);
        }

        // 3. REUSE DETECTED. This token was already rotated, and an honest client never presents
        //    a rotated token — it discards it the moment it receives the successor. So a copy is
        //    in circulation, and both the attacker and the legitimate user hold tokens from this
        //    lineage. Revoking only the presented one would leave whichever party holds the live
        //    successor authenticated, and there is no way to tell which party that is. Killing
        //    the family is the only safe answer: both are forced to sign in again, and only the
        //    one who knows the password can.
        if (stored.RevokedAt is not null)
        {
            int revokedCount = await RevokeFamilyOutOfBandAsync(stored.FamilyId, now, cancellationToken);

            logger.LogWarning(
                "Refresh token reuse detected for user {UserId} from {IpAddress}. "
                + "Revoked {RevokedCount} token(s) in family {FamilyId}.",
                stored.UserId,
                ipAddress,
                revokedCount,
                stored.FamilyId);

            return Result.Failure<TokenPair>(AuthErrors.InvalidRefreshToken);
        }

        // 4. Expired. Not suspicious — just old.
        if (stored.ExpiresAt <= now)
        {
            return Result.Failure<TokenPair>(AuthErrors.InvalidRefreshToken);
        }

        // The user is re-read rather than trusted from the old token, so a deletion or a role
        // change takes effect on the next refresh instead of at the end of the refresh window.
        AuthenticatedUser? user = await identityService.FindByIdAsync(stored.UserId, cancellationToken);

        if (user is null)
        {
            await RevokeFamilyOutOfBandAsync(stored.FamilyId, now, cancellationToken);
            return Result.Failure<TokenPair>(AuthErrors.InvalidRefreshToken);
        }

        // 5. Rotate: revoke the presented token, mint a successor in the same family, link them.
        (RefreshToken successor, string rawToken) = stored.Rotate(
            now,
            _jwt.RefreshTokenLifetime,
            ipAddress,
            userAgent);

        dbContext.RefreshTokens.Add(successor);

        // Both writes in one SaveChanges. The pipeline's transaction behaviour wraps the command,
        // so a crash cannot leave the presented token revoked with no successor — which would log
        // an honest user out for no reason.
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new TokenPair(
            CreateAccessToken(user),
            new IssuedRefreshToken(rawToken, successor.ExpiresAt),
            user));
    }

    public async Task RevokeFamilyAsync(string presentedToken, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string presentedHash = RefreshToken.Hash(presentedToken);

        RefreshToken? stored = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(token => token.TokenHash == presentedHash, cancellationToken);

        // Logging out with an unknown or already-dead token is a no-op, not an error. The caller's
        // desired end state already holds.
        if (stored is null)
        {
            return;
        }

        await RevokeFamilyAsync(dbContext, stored.FamilyId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Revokes a family on a <b>separate connection that commits independently</b> of the request's
    /// transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This indirection is load-bearing, and the obvious version is wrong.</b> Reuse detection
    /// ends by returning <c>Result.Failure</c>, and <c>CommandTransactionBehavior</c> rolls the
    /// transaction back on any failure — so revoking the family on the request's own
    /// <c>DbContext</c> revokes it, returns 401, and then <b>throws the revocation away</b>. The
    /// attacker's stolen successor token keeps working, which is the exact opposite of what reuse
    /// detection is for.
    /// </para>
    /// <para>
    /// This was not caught by reasoning about it. It was caught by replaying a rotated token
    /// against a running server and watching the supposedly dead successor return 200.
    /// </para>
    /// <para>
    /// A fresh scope gives a fresh <c>DbContext</c> on its own connection, with no part in the
    /// caller's transaction, so its commit survives the rollback. That is correct precisely
    /// because the two are independent: revoking a leaked lineage is a security action that must
    /// not be conditional on the request that revealed it succeeding.
    /// </para>
    /// </remarks>
    private async Task<int> RevokeFamilyOutOfBandAsync(
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        ApplicationDbContext isolated =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        int revoked = await RevokeFamilyAsync(isolated, familyId, now, cancellationToken);

        await isolated.SaveChangesAsync(cancellationToken);

        return revoked;
    }

    /// <summary>
    /// Revokes every live token in a family on the given context. Returns how many were affected,
    /// for the security log. Does not save — the caller decides which transaction that belongs to.
    /// </summary>
    /// <remarks>
    /// Loaded and mutated through the change tracker rather than issued as an
    /// <c>ExecuteUpdateAsync</c>, so the audit interceptor stamps <c>updated_at</c> on each row.
    /// A family holds a handful of tokens at most — one per refresh over fourteen days — so the
    /// round trip is not worth optimising away, and losing the audit trail on a security event
    /// would be a poor trade.
    /// </remarks>
    private static async Task<int> RevokeFamilyAsync(
        ApplicationDbContext context,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<RefreshToken> family = await context.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (RefreshToken token in family)
        {
            token.Revoke(now);
        }

        return family.Count;
    }
}
