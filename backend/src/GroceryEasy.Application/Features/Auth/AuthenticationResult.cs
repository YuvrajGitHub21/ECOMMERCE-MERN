using GroceryEasy.Application.Abstractions.Identity;

namespace GroceryEasy.Application.Features.Auth;

/// <summary>
/// What a successful login or refresh produces, before the API decides how to deliver it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is not the response body.</b> It carries the refresh token, and the refresh token
/// must never be serialised into a response — it leaves the API only as an <c>HttpOnly</c> cookie
/// that page script cannot read. The endpoint splits this in two: the cookie gets
/// <see cref="RefreshToken"/>, the JSON body gets everything else.
/// </para>
/// <para>
/// <b>Why a separate type rather than a <c>[JsonIgnore]</c> property on the response.</b> That
/// works right up until someone removes the attribute during a refactor, and nothing fails when
/// they do — the token simply starts appearing in every login response, silently. Keeping the
/// value out of the serialised type entirely means it cannot leak by omission. An integration
/// test also asserts the raw response body does not contain it.
/// </para>
/// </remarks>
/// <param name="AccessToken">The signed access token and its expiry.</param>
/// <param name="RefreshToken">The opaque refresh token. Cookie only, never the body.</param>
/// <param name="User">The authenticated user, for the response body.</param>
public sealed record AuthenticationResult(
    AccessToken AccessToken,
    IssuedRefreshToken RefreshToken,
    AuthenticatedUser User);
