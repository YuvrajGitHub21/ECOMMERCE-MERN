using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GroceryEasy.Infrastructure.Identity;
using GroceryEasy.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GroceryEasy.IntegrationTests.Auth;

/// <summary>
/// Registration, verification, login and the enumeration-resistance properties.
/// </summary>
public sealed class AuthenticationTests(IntegrationTestWebAppFactory factory)
    : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Register_ThenVerify_ThenLogin_Succeeds()
    {
        TestUser user = await RegisterVerifiedUserAsync();

        LoginOutcome login = await LoginAsync(user);

        login.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>An unverified account cannot log in, and is not told that is the reason.</summary>
    [Fact]
    public async Task Login_BeforeVerifying_IsRejectedIndistinguishably()
    {
        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = "raj@example.com", password = "correct-horse-battery", fullName = "Raj Menon" },
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "raj@example.com", password = "correct-horse-battery" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The email column is <c>citext</c>, so casing cannot create a second account.
    /// </summary>
    [Fact]
    public async Task Register_WithAnExistingEmailInDifferentCasing_Conflicts()
    {
        await RegisterVerifiedUserAsync("asha@example.com");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = "ASHA@Example.COM", password = "correct-horse-battery", fullName = "Someone Else" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        int users = await QueryDatabaseAsync(db => db.Users.CountAsync());
        users.ShouldBe(1);
    }

    /// <summary>
    /// An unknown email and a wrong password produce byte-identical responses.
    /// </summary>
    /// <remarks>
    /// Not merely "both 401". A differing body or header is the same user-enumeration leak in a
    /// quieter form, so the bodies are compared directly. Only the trace identifier, which is
    /// per-request by construction, is excluded.
    /// </remarks>
    [Fact]
    public async Task Login_UnknownEmailAndWrongPassword_AreIndistinguishable()
    {
        TestUser user = await RegisterVerifiedUserAsync();

        HttpResponseMessage unknown = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "nobody@example.com", password = "correct-horse-battery" },
            TestContext.Current.CancellationToken);

        HttpResponseMessage wrongPassword = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = "not-the-right-password" },
            TestContext.Current.CancellationToken);

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongPassword.StatusCode.ShouldBe(wrongPassword.StatusCode);

        string unknownBody = Scrub(await unknown.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));
        string wrongPasswordBody = Scrub(await wrongPassword.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));

        unknownBody.ShouldBe(wrongPasswordBody);
    }

    /// <summary>
    /// Forgot-password answers identically whether or not the account exists.
    /// </summary>
    /// <remarks>
    /// This is the direct regression test for L-11, where the legacy endpoint returned a "user not
    /// found" error and was therefore a free membership oracle for any address list.
    /// </remarks>
    [Fact]
    public async Task ForgotPassword_KnownAndUnknownEmail_AreIndistinguishable()
    {
        TestUser user = await RegisterVerifiedUserAsync();

        HttpResponseMessage known = await Client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email = user.Email },
            TestContext.Current.CancellationToken);

        HttpResponseMessage unknown = await Client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email = "nobody@example.com" },
            TestContext.Current.CancellationToken);

        known.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        string knownBody = await known.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string unknownBody = await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        knownBody.ShouldBe(unknownBody);
    }

    /// <summary>The reset link is built from configuration, never from the request's Host header.</summary>
    [Fact]
    public async Task ForgotPassword_BuildsTheLinkFromTheConfiguredFrontendAddress()
    {
        TestUser user = await RegisterVerifiedUserAsync();
        Emails.Clear();

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new { email = user.Email }),
        };

        // A hostile Host header. If any link were built from it, this is where it would show up.
        request.Headers.Host = "evil.example.com";

        await Client.SendAsync(request, TestContext.Current.CancellationToken);

        SentEmail message = Emails.LastTo(user.Email).ShouldNotBeNull();

        message.HtmlBody.ShouldContain("https://app.groceryeasy.local");
        message.HtmlBody.ShouldNotContain("evil.example.com");
    }

    /// <summary>A full password reset, then logging in with the new password.</summary>
    [Fact]
    public async Task ResetPassword_WithTheEmailedToken_ChangesThePassword()
    {
        TestUser user = await RegisterVerifiedUserAsync();
        Emails.Clear();

        await Client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email = user.Email },
            TestContext.Current.CancellationToken);

        SentEmail message = Emails.LastTo(user.Email).ShouldNotBeNull();
        string token = CollectingEmailSender.ExtractLinkParameter(message.HtmlBody, "token");

        HttpResponseMessage reset = await Client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { email = user.Email, token, newPassword = "a-completely-different-one" },
            TestContext.Current.CancellationToken);

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage withNewPassword = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = "a-completely-different-one" },
            TestContext.Current.CancellationToken);

        withNewPassword.StatusCode.ShouldBe(HttpStatusCode.OK);

        HttpResponseMessage withOldPassword = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = user.Password },
            TestContext.Current.CancellationToken);

        withOldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>A validation failure produces ProblemDetails with a populated errors member.</summary>
    /// <remarks>
    /// The regression test for L-17, where the server's error shape and the client's expectation
    /// disagreed and every error in the application rendered as the word "undefined".
    /// </remarks>
    [Fact]
    public async Task Register_WithAnInvalidPayload_ReturnsProblemDetailsWithPerFieldErrors()
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = "not-an-email", password = "short", fullName = "" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain("\"errors\"");
        body.ShouldContain("Email");
        body.ShouldContain("Password");
        body.ShouldContain("FullName");
        body.ShouldContain("Validation.Failed");
    }

    /// <summary>
    /// A still-valid token for a deleted account returns 401, not 500.
    /// </summary>
    /// <remarks>
    /// The regression test for L-07. In the legacy system a deleted user's token kept
    /// authenticating and the request then died on a null dereference — an authentication problem
    /// surfacing as a server crash. Here the security stamp check rejects it at the door.
    /// </remarks>
    [Fact]
    public async Task DeletedUser_WithAStillValidAccessToken_Gets401NotServerError()
    {
        TestUser user = await RegisterVerifiedUserAsync();
        LoginOutcome login = await LoginAsync(user);

        using (HttpRequestMessage before = new(HttpMethod.Get, "/api/users/me"))
        {
            before.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            HttpResponseMessage worked = await Client.SendAsync(
                before,
                TestContext.Current.CancellationToken);
            worked.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await QueryDatabaseAsync(async db =>
        {
            ApplicationUser stored = await db.Users.SingleAsync(u => u.Email == user.Email);
            db.Users.Remove(stored);
            return await db.SaveChangesAsync();
        });

        using HttpRequestMessage after = new(HttpMethod.Get, "/api/users/me");
        after.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        HttpResponseMessage response = await Client.SendAsync(
            after,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutAToken_IsUnauthorized()
    {
        HttpResponseMessage response = await Client.GetAsync(
            "/api/users/me",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithAValidToken_ReturnsTheSignedInUser()
    {
        TestUser user = await RegisterVerifiedUserAsync();
        LoginOutcome login = await LoginAsync(user);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        HttpResponseMessage response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain(user.Email);
        body.ShouldContain("Customer");
    }

    /// <summary>Removes the per-request trace identifier so two bodies can be compared.</summary>
    private static string Scrub(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            body,
            "\"traceId\":\"[^\"]*\"",
            "\"traceId\":\"<scrubbed>\"");
}
