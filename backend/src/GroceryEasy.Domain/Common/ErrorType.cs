namespace GroceryEasy.Domain.Common;

/// <summary>
/// The kind of an <see cref="Error"/>. The API layer maps each member onto exactly one
/// HTTP status code, which is why this enum lives in Domain rather than being an API
/// concern: a handler decides that "this email is already taken" is a conflict, and the
/// transport decides that a conflict is 409.
/// </summary>
public enum ErrorType
{
    /// <summary>An unexpected failure. Maps to 500.</summary>
    Failure = 0,

    /// <summary>The request was well-formed but its contents were rejected. Maps to 400.</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist, or the caller may not know that it does. Maps to 404.</summary>
    NotFound = 2,

    /// <summary>The request conflicts with the current state of the resource. Maps to 409.</summary>
    Conflict = 3,

    /// <summary>The caller is authenticated but not permitted. Maps to 403.</summary>
    Forbidden = 4,

    /// <summary>The caller is not authenticated, or their credentials were rejected. Maps to 401.</summary>
    Unauthorized = 5,
}
