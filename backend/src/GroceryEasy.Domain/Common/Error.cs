using System.Diagnostics.CodeAnalysis;

namespace GroceryEasy.Domain.Common;

/// <summary>
/// An expected failure, carried by a <see cref="Result"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Code"/> is the stable, machine-readable identifier — it is what a client
/// switches on, and what ends up in the <c>type</c> URI of the ProblemDetails response.
/// It must never change once shipped. <see cref="Description"/> is for humans and may be
/// reworded freely.
/// </para>
/// <para>
/// Descriptions are read by end users, so they must not leak whether a given account
/// exists. See the login and forgot-password flows, where that is the whole point.
/// </para>
/// </remarks>
/// <para>
/// Not <see langword="sealed"/>, for exactly one reason: <see cref="ValidationError"/> needs to
/// carry a list of per-field failures so the API can render a populated RFC 9457 <c>errors</c>
/// member, and a <see cref="Result"/> holds only one <see cref="Error"/>. That is the only
/// derived type there should ever be.
/// </para>
/// <param name="Code">Stable machine-readable identifier, e.g. <c>Auth.EmailAlreadyInUse</c>.</param>
/// <param name="Description">Human-readable explanation.</param>
/// <param name="Type">The kind of failure, which determines the HTTP status code.</param>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification =
        "CA1716 protects consumers writing VB, where 'Error' is a statement keyword. This " +
        "assembly is consumed only by the C# projects in this solution and is never shipped " +
        "as a library, so the cross-language concern does not apply. 'Error' is the name the " +
        "whole codebase reads best, and every alternative (Err, Fault, ErrorInfo) is worse.")]
public record Error(string Code, string Description, ErrorType Type)
{
    /// <summary>
    /// The absence of an error. A successful <see cref="Result"/> always carries this, and
    /// a failed one never does — <see cref="Result"/>'s constructor enforces both.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Creates a <see cref="ErrorType.NotFound"/> error.</summary>
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    /// <summary>Creates a <see cref="ErrorType.Validation"/> error.</summary>
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    /// <summary>Creates a <see cref="ErrorType.Conflict"/> error.</summary>
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    /// <summary>Creates a <see cref="ErrorType.Unauthorized"/> error.</summary>
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    /// <summary>Creates a <see cref="ErrorType.Forbidden"/> error.</summary>
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    /// <summary>Creates a <see cref="ErrorType.Failure"/> error.</summary>
    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
}
