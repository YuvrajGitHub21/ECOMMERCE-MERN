using GroceryEasy.Api.Authentication;
using GroceryEasy.Api.Extensions;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Application.Features.Auth;
using GroceryEasy.Application.Features.Auth.ForgotPassword;
using GroceryEasy.Application.Features.Auth.Login;
using GroceryEasy.Application.Features.Auth.Logout;
using GroceryEasy.Application.Features.Auth.Refresh;
using GroceryEasy.Application.Features.Auth.Register;
using GroceryEasy.Application.Features.Auth.ResetPassword;
using GroceryEasy.Application.Features.Auth.VerifyEmail;
using GroceryEasy.Domain.Common;

namespace GroceryEasy.Api.Endpoints;

/// <summary>
/// The eight authentication endpoints, grouped under <c>/api/auth</c>.
/// </summary>
/// <remarks>
/// The group prefix must stay in step with <see cref="RefreshTokenCookie.Path"/>: the refresh
/// cookie is scoped to that path, so moving these routes without moving the cookie silently stops
/// the browser from sending it and every refresh starts failing.
/// </remarks>
internal sealed class AuthEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app
            .MapGroup(RefreshTokenCookie.Path)
            .WithTags("Authentication");

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .WithSummary("Creates an account and sends a verification email.");

        group.MapPost("/verify-email", VerifyEmailAsync)
            .AllowAnonymous()
            .WithSummary("Confirms an email address using the token from the emailed link.");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithSummary("Signs in and returns an access token, setting the refresh cookie.");

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithSummary("Rotates the refresh cookie and returns a new access token.");

        // POST, never GET. A logout reachable by GET is triggered by any image tag or prefetch
        // a third-party page puts in front of the user (L-10).
        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithSummary("Revokes the current refresh-token family and clears the cookie.");

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .AllowAnonymous()
            .WithSummary("Sends a reset link. Always succeeds, whether or not the account exists.");

        group.MapPost("/reset-password", ResetPasswordAsync)
            .AllowAnonymous()
            .WithSummary("Sets a new password using the token from the emailed link.");
    }

    private static async Task<IResult> RegisterAsync(
        RegisterCommand command,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<Unit> result = await dispatcher.Send(command, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailCommand command,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<Unit> result = await dispatcher.Send(command, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> LoginAsync(
        LoginCommand command,
        IDispatcher dispatcher,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<AuthenticationResult> result = await dispatcher.Send(command, cancellationToken);

        return WriteAuthentication(result, context);
    }

    private static async Task<IResult> RefreshAsync(
        IDispatcher dispatcher,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // The token comes from the cookie and never from the body. A refresh endpoint that
        // accepts the token in a request body is one an attacker with a stolen copy can call from
        // anywhere, which throws away everything HttpOnly and SameSite were bought for.
        RefreshCommand command = new(RefreshTokenCookie.Read(context));

        Result<AuthenticationResult> result = await dispatcher.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            // Clear on failure. The cookie is known bad — expired, revoked, or a detected reuse —
            // so leaving it in place means the browser presents it again on every retry.
            RefreshTokenCookie.Clear(context);
        }

        return WriteAuthentication(result, context);
    }

    private static async Task<IResult> LogoutAsync(
        IDispatcher dispatcher,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        LogoutCommand command = new(RefreshTokenCookie.Read(context));

        _ = await dispatcher.Send(command, cancellationToken);

        RefreshTokenCookie.Clear(context);

        // Always 204, even with no cookie or an unknown one. The caller wanted no session and
        // now has none.
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordCommand command,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<Unit> result = await dispatcher.Send(command, cancellationToken);

        // 204 whether or not the address is registered. An endpoint that answers differently for
        // a known and an unknown address is a free membership oracle (L-11). The integration test
        // asserts the two responses are byte-identical.
        return result.IsSuccess ? TypedResults.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordCommand command,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<Unit> result = await dispatcher.Send(command, cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToHttpResult();
    }

    /// <summary>
    /// Splits an <see cref="AuthenticationResult"/> across the cookie and the response body.
    /// </summary>
    /// <remarks>
    /// <b>The refresh token goes into the cookie and never into the body.</b> This is the only
    /// function that touches both, which is what makes that rule checkable by reading one place.
    /// </remarks>
    private static IResult WriteAuthentication(Result<AuthenticationResult> result, HttpContext context)
    {
        if (result.IsFailure)
        {
            return result.ToHttpResult();
        }

        AuthenticationResult authentication = result.Value;

        // Cookie lifetime comes from the token itself rather than a duration repeated here.
        // Two places deciding how long a session lasts drift the first time either is tuned,
        // leaving the browser presenting a cookie the server already rejects — or discarding one
        // it would still have accepted.
        RefreshTokenCookie.Write(
            context,
            authentication.RefreshToken.Value,
            authentication.RefreshToken.ExpiresAt);

        return TypedResults.Ok(new AuthenticationResponse(
            authentication.AccessToken.Value,
            authentication.AccessToken.ExpiresAt,
            authentication.User.Id,
            authentication.User.Email,
            authentication.User.FullName,
            authentication.User.Roles));
    }
}

/// <summary>
/// The login and refresh response body.
/// </summary>
/// <remarks>
/// Note what is absent: the refresh token. It leaves the API only as an <c>HttpOnly</c> cookie.
/// </remarks>
/// <param name="AccessToken">Short-lived token. The client holds it in memory only, never in <c>localStorage</c>.</param>
/// <param name="ExpiresAt">So the client can refresh ahead of a failure rather than after one.</param>
/// <param name="UserId">The signed-in user.</param>
/// <param name="Email">Email address.</param>
/// <param name="FullName">Display name.</param>
/// <param name="Roles">For rendering navigation only — never for an access decision (L-06).</param>
internal sealed record AuthenticationResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);
