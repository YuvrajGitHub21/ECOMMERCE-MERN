using GroceryEasy.Domain.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GroceryEasy.Api.Extensions;

/// <summary>
/// The single place a <see cref="Result"/> becomes an HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// <b>One error shape, always: RFC 9457 ProblemDetails.</b> This is the permanent fix for L-17.
/// The legacy API returned <c>{ success: false, error: "..." }</c> while every client read
/// <c>error.response.data.message</c> — a field that did not exist — so <b>every error message in
/// the application rendered the word <c>undefined</c></b>. Nothing caught it because nothing
/// described the contract in one place. This function is that place, and the generated
/// TypeScript client in Phase 3 is generated from it.
/// </para>
/// <para>
/// <b>The status mapping lives on <see cref="ErrorType"/>, not on the endpoint.</b> An endpoint
/// that picks its own status code is an endpoint that eventually picks a different one for the
/// same condition. A handler says what kind of failure happened; this decides what that means
/// over HTTP.
/// </para>
/// </remarks>
public static class ResultExtensions
{
    private const string ProblemTypeBaseUri = "https://groceryeasy.dev/errors/";

    /// <summary>Maps a valueless result: 204 on success, ProblemDetails on failure.</summary>
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : Problem(result.Error);

    /// <summary>Maps a result carrying a value: 200 with the value, or ProblemDetails.</summary>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error);

    /// <summary>Maps a result carrying a value through a projection, so the response body can
    /// differ from what the handler returned — which is how the refresh token stays out of it.</summary>
    public static IResult ToHttpResult<TValue, TResponse>(
        this Result<TValue> result,
        Func<TValue, TResponse> toResponse)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(toResponse);

        return result.IsSuccess
            ? TypedResults.Ok(toResponse(result.Value))
            : Problem(result.Error);
    }

    /// <summary>The <see cref="ErrorType"/> to status-code mapping, in one table.</summary>
    public static int ToStatusCode(this ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static ProblemHttpResult Problem(Error error)
    {
        int statusCode = error.Type.ToStatusCode();

        Dictionary<string, object?> extensions = new(StringComparer.Ordinal)
        {
            // Stable and machine-readable, so a client can special-case one failure without
            // matching on English prose that a copy edit will change.
            ["code"] = error.Code,
        };

        // A validation failure carries per-field detail. The shape matches what
        // react-hook-form expects, so the frontend normaliser can hand it straight to the form
        // rather than reshaping it — see the ProblemDetails normaliser task in Phase 3.
        if (error is ValidationError validationError)
        {
            extensions["errors"] = validationError.Errors
                .GroupBy(e => e.Code, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(e => e.Description).ToArray(),
                    StringComparer.Ordinal);
        }

        return TypedResults.Problem(
            detail: error.Description,
            statusCode: statusCode,
            title: TitleFor(error.Type),
            type: ProblemTypeBaseUri + error.Type.ToString().ToLowerInvariant(),
            extensions: extensions);
    }

    private static string TitleFor(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => "One or more validation errors occurred.",
        ErrorType.Unauthorized => "Authentication is required.",
        ErrorType.Forbidden => "You do not have access to this resource.",
        ErrorType.NotFound => "The requested resource was not found.",
        ErrorType.Conflict => "The request conflicts with the current state.",
        _ => "An unexpected error occurred.",
    };
}
