namespace GroceryEasy.Domain.Common;

/// <summary>
/// An <see cref="Error"/> that carries the individual field failures behind it.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Result"/> holds one <see cref="Error"/>, but a rejected request usually has
/// several things wrong with it and the client needs all of them at once — one round trip per
/// bad field is poor form. Each entry's <see cref="Error.Code"/> is the offending field name
/// and its <see cref="Error.Description"/> is the message, which the API layer groups into the
/// RFC 9457 <c>errors</c> member.
/// </para>
/// <para>
/// This is the shape that closes legacy defect L-17, where the server answered
/// <c>{ success, error }</c> while every client read <c>.message</c>, so every error toast in
/// the application displayed the word <c>undefined</c>. There is now one response shape, and
/// it is generated into the client's types.
/// </para>
/// </remarks>
public sealed record ValidationError : Error
{
    /// <summary>The stable identifier every validation failure shares.</summary>
    public const string DefaultCode = "Validation.Failed";

    /// <summary>Creates a validation error from the individual field failures.</summary>
    public ValidationError(IReadOnlyList<Error> errors)
        : base(DefaultCode, "One or more validation errors occurred.", ErrorType.Validation)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    /// <summary>The individual failures: <c>Code</c> is the field, <c>Description</c> the message.</summary>
    public IReadOnlyList<Error> Errors { get; }
}
