using System.Net;
using GroceryEasy.Infrastructure.Identity;
using GroceryEasy.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GroceryEasy.IntegrationTests.Auth;

/// <summary>
/// Refresh-token rotation and reuse detection, against a real database.
/// </summary>
/// <remarks>
/// These are the tests this phase exists to make pass, and the reuse-detection one is named in
/// the README as evidence.
/// </remarks>
public sealed class RefreshTokenRotationTests(IntegrationTestWebAppFactory factory)
    : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Login_ReturnsAnAccessTokenAndSetsTheRefreshCookie()
    {
        TestUser user = await RegisterVerifiedUserAsync();

        LoginOutcome login = await LoginAsync(user);

        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        login.RefreshToken.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The refresh token must never appear in the response body.
    /// </summary>
    /// <remarks>
    /// Asserted on the raw text rather than on a deserialised shape, because the failure this
    /// guards against is a property being <em>added</em> to the response — which a typed
    /// assertion would not notice.
    /// </remarks>
    [Fact]
    public async Task Login_DoesNotPutTheRefreshTokenInTheResponseBody()
    {
        TestUser user = await RegisterVerifiedUserAsync();

        LoginOutcome login = await LoginAsync(user);

        string body = await login.Response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        body.ShouldNotContain(login.RefreshToken!);
    }

    /// <summary>The refresh cookie carries the flags that make it worth using at all.</summary>
    [Fact]
    public async Task Login_SetsTheRefreshCookieHttpOnlyAndScopedToTheAuthPath()
    {
        TestUser user = await RegisterVerifiedUserAsync();

        LoginOutcome login = await LoginAsync(user);

        string cookie = login.Response.Headers
            .GetValues("Set-Cookie")
            .First(value => value.StartsWith("ge_refresh=", StringComparison.Ordinal));

        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/auth", Case.Insensitive);
    }

    [Fact]
    public async Task Refresh_IssuesANewTokenAndRevokesThePresentedOne()
    {
        TestUser user = await RegisterVerifiedUserAsync();
        LoginOutcome login = await LoginAsync(user);

        HttpResponseMessage refreshed = await RefreshAsync(login.RefreshToken!);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        string? rotated = ReadRefreshCookie(refreshed);
        rotated.ShouldNotBeNullOrWhiteSpace();
        rotated.ShouldNotBe(login.RefreshToken);

        string presentedHash = RefreshToken.Hash(login.RefreshToken!);

        RefreshToken? stored = await QueryDatabaseAsync(db => db.RefreshTokens
            .SingleOrDefaultAsync(token => token.TokenHash == presentedHash));

        stored.ShouldNotBeNull();
        stored.RevokedAt.ShouldNotBeNull();
        stored.ReplacedByTokenId.ShouldNotBeNull();
    }

    /// <summary>
    /// Replaying an already-rotated refresh token revokes the entire family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the test the phase is judged on.</b> An honest client discards a refresh token
    /// the instant it receives the successor, so presenting a rotated one means a copy leaked and
    /// two parties now hold tokens from the same lineage. There is no way to tell which is the
    /// legitimate user, so the only safe answer is to kill the lineage: both are forced to sign in
    /// again, and only the one who knows the password can.
    /// </para>
    /// <para>
    /// The second half — asserting the <em>successor</em> also stops working — is the half that
    /// matters and the half that caught a real bug. Reuse detection ends by returning a failure,
    /// and the pipeline's transaction behaviour rolls back on failure, so the first implementation
    /// revoked the family and then discarded the revocation. The replay returned 401 and the
    /// attacker's token kept working. A test that only checked the replay would have passed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Refresh_WithAnAlreadyRotatedToken_RevokesTheWholeFamily()
    {
        TestUser user = await RegisterVerifiedUserAsync();
        LoginOutcome login = await LoginAsync(user);

        // Rotate once. The successor is what a legitimate client would now be holding.
        HttpResponseMessage firstRotation = await RefreshAsync(login.RefreshToken!);
        string successor = ReadRefreshCookie(firstRotation)!;

        // Replay the original — this is the leaked copy being used.
        HttpResponseMessage replay = await RefreshAsync(login.RefreshToken!);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The successor must now be dead too, or the attacker simply carries on with it.
        HttpResponseMessage successorAttempt = await RefreshAsync(successor);
        successorAttempt.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And nothing in the lineage is left live in the database.
        int liveTokens = await QueryDatabaseAsync(db => db.RefreshTokens
            .CountAsync(token => token.RevokedAt == null));

        liveTokens.ShouldBe(0);
    }

    [Fact]
    public async Task Refresh_WithAnUnknownToken_IsRejected()
    {
        await RegisterVerifiedUserAsync();

        HttpResponseMessage response = await RefreshAsync("not-a-token-anyone-ever-issued");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithNoCookieAtAll_IsRejected()
    {
        HttpResponseMessage response = await Client.PostAsync(
            "/api/auth/refresh",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Logout ends the whole family, not just the token that was presented.</summary>
    [Fact]
    public async Task Logout_RevokesTheFamilyAndTheTokenStopsWorking()
    {
        TestUser user = await RegisterVerifiedUserAsync();
        LoginOutcome login = await LoginAsync(user);

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"ge_refresh={login.RefreshToken}");

        HttpResponseMessage logout = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage afterLogout = await RefreshAsync(login.RefreshToken!);
        afterLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Logging out twice, or with no session, is still a success.</summary>
    [Fact]
    public async Task Logout_WithNoCookie_StillSucceeds()
    {
        HttpResponseMessage response = await Client.PostAsync(
            "/api/auth/logout",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>Two logins are two independent lineages, so logging out of one leaves the other.</summary>
    [Fact]
    public async Task TwoLogins_ProduceIndependentFamilies()
    {
        TestUser user = await RegisterVerifiedUserAsync();

        LoginOutcome first = await LoginAsync(user);
        LoginOutcome second = await LoginAsync(user);

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Cookie", $"ge_refresh={first.RefreshToken}");
        await Client.SendAsync(request, TestContext.Current.CancellationToken);

        HttpResponseMessage secondStillWorks = await RefreshAsync(second.RefreshToken!);

        secondStillWorks.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
