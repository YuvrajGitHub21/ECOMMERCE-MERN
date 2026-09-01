using System.Net.Http.Json;
using GroceryEasy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryEasy.IntegrationTests.Infrastructure;

/// <summary>
/// Marks the collection every database-backed test belongs to.
/// </summary>
/// <remarks>
/// xUnit runs collections in parallel and the tests inside one serially. Putting every
/// integration test in a single collection is what makes the Respawn reset safe: two tests
/// truncating the same tables concurrently would delete each other's fixtures, producing failures
/// that move around between runs.
/// </remarks>
[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit's collection-definition convention names this type after the collection "
        + "it defines. It is not, and is not meant to be, an IEnumerable.")]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestWebAppFactory>
{
    /// <summary>The collection name.</summary>
    public const string Name = "Integration";
}

/// <summary>
/// Shared plumbing: a client, a clean database per test, and helpers for the auth flow.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase(IntegrationTestWebAppFactory factory) : IAsyncLifetime
{
    /// <summary>The test host.</summary>
    protected IntegrationTestWebAppFactory Factory { get; } = factory;

    /// <summary>
    /// A client that does <b>not</b> follow redirects and does not persist cookies between calls.
    /// </summary>
    /// <remarks>
    /// Cookie handling is explicit in these tests rather than automatic, because the refresh
    /// cookie is the thing under test — a client that quietly manages it would hide whether the
    /// server set it, cleared it, or scoped it correctly.
    /// </remarks>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>Captured outbound mail.</summary>
    protected CollectingEmailSender Emails => Factory.Emails;

    public async ValueTask InitializeAsync()
    {
        await Factory.ResetDatabaseAsync();

        Client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Runs work against a fresh database context, for asserting on stored state.</summary>
    protected async Task<T> QueryDatabaseAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        using IServiceScope scope = Factory.Services.CreateScope();

        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    /// <summary>Registers a user and completes email verification, leaving an account able to log in.</summary>
    protected async Task<TestUser> RegisterVerifiedUserAsync(
        string email = "asha@example.com",
        string password = "correct-horse-battery",
        string fullName = "Asha Iyer")
    {
        HttpResponseMessage registration = await Client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password, fullName },
            TestContext.Current.CancellationToken);

        registration.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        SentEmail message = Emails.LastTo(email)
            ?? throw new InvalidOperationException("No verification email was sent.");

        string token = CollectingEmailSender.ExtractLinkParameter(message.HtmlBody, "token");

        HttpResponseMessage verification = await Client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new { email, token },
            TestContext.Current.CancellationToken);

        verification.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        return new TestUser(email, password, fullName);
    }

    /// <summary>Logs in and returns the access token plus the raw refresh cookie value.</summary>
    protected async Task<LoginOutcome> LoginAsync(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = user.Password },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        AuthBody body = (await response.Content.ReadFromJsonAsync<AuthBody>(
            TestContext.Current.CancellationToken))!;

        return new LoginOutcome(body.AccessToken, ReadRefreshCookie(response), response);
    }

    /// <summary>Calls refresh with an explicit cookie value, bypassing any client cookie state.</summary>
    protected async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"ge_refresh={refreshToken}");

        return await Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Pulls the refresh cookie's value out of a response's Set-Cookie header.</summary>
    protected static string? ReadRefreshCookie(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            return null;
        }

        string? cookie = cookies.FirstOrDefault(value =>
            value.StartsWith("ge_refresh=", StringComparison.Ordinal));

        if (cookie is null)
        {
            return null;
        }

        string value = cookie["ge_refresh=".Length..].Split(';')[0];

        return string.IsNullOrEmpty(value) ? null : value;
    }
}

/// <summary>A registered, verified account.</summary>
/// <param name="Email">Address.</param>
/// <param name="Password">Plain-text password, for logging in.</param>
/// <param name="FullName">Display name.</param>
public sealed record TestUser(string Email, string Password, string FullName);

/// <summary>The result of a login.</summary>
/// <param name="AccessToken">The signed token.</param>
/// <param name="RefreshToken">The raw cookie value, or null if none was set.</param>
/// <param name="Response">The raw response, for asserting on headers and body.</param>
public sealed record LoginOutcome(string AccessToken, string? RefreshToken, HttpResponseMessage Response);

/// <summary>The login and refresh response body, for deserialisation in tests.</summary>
/// <param name="AccessToken">The signed token.</param>
/// <param name="ExpiresAt">Expiry.</param>
/// <param name="UserId">User identifier.</param>
/// <param name="Email">Email address.</param>
/// <param name="FullName">Display name.</param>
/// <param name="Roles">Role names.</param>
public sealed record AuthBody(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);
