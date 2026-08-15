using GroceryEasy.Domain.Common;

namespace GroceryEasy.Domain.UnitTests.Common;

public sealed class ErrorTests
{
    [Theory]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.Failure)]
    public void Factory_SetsTheMatchingType(ErrorType type)
    {
        Error error = Create(type, "Some.Code", "Some description.");

        error.Type.ShouldBe(type);
        error.Code.ShouldBe("Some.Code");
        error.Description.ShouldBe("Some description.");
    }

    [Fact]
    public void None_IsEmpty()
    {
        Error.None.Code.ShouldBeEmpty();
        Error.None.Description.ShouldBeEmpty();
    }

    [Fact]
    public void Errors_WithTheSameParts_AreEqual()
    {
        // Result's invariant compares against Error.None by value, so record equality
        // is load-bearing rather than incidental.
        Error left = Error.Conflict("Auth.EmailAlreadyInUse", "That email is already registered.");
        Error right = Error.Conflict("Auth.EmailAlreadyInUse", "That email is already registered.");

        left.ShouldBe(right);
    }

    [Fact]
    public void Errors_DifferingOnlyByType_AreNotEqual()
    {
        Error notFound = Error.NotFound("Same.Code", "Same description.");
        Error conflict = Error.Conflict("Same.Code", "Same description.");

        notFound.ShouldNotBe(conflict);
    }

    [Fact]
    public void AnEmptyError_IsNotNone()
    {
        // Error.None is identified by its parts, not by reference, so an error that
        // happens to be blank but is not None must still be distinguishable.
        Error blankValidation = Error.Validation(string.Empty, string.Empty);

        blankValidation.ShouldNotBe(Error.None);
    }

    private static Error Create(ErrorType type, string code, string description) => type switch
    {
        ErrorType.NotFound => Error.NotFound(code, description),
        ErrorType.Validation => Error.Validation(code, description),
        ErrorType.Conflict => Error.Conflict(code, description),
        ErrorType.Unauthorized => Error.Unauthorized(code, description),
        ErrorType.Forbidden => Error.Forbidden(code, description),
        ErrorType.Failure => Error.Failure(code, description),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
