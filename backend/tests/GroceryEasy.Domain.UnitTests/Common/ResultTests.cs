using GroceryEasy.Domain.Common;

namespace GroceryEasy.Domain.UnitTests.Common;

public sealed class ResultTests
{
    private static readonly Error _sampleError =
        Error.Conflict("Test.Conflict", "Something already exists.");

    [Fact]
    public void Success_IsSuccessful()
    {
        Result result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Success_CarriesNoError()
    {
        Result result = Result.Success();

        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_IsNotSuccessful()
    {
        Result result = Result.Failure(_sampleError);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Failure_CarriesTheError()
    {
        Result result = Result.Failure(_sampleError);

        result.Error.ShouldBe(_sampleError);
    }

    [Fact]
    public void Failure_WithNoError_Throws()
    {
        // A failure that cannot say what failed is a bug in the calling factory,
        // not an expected outcome, so the invariant throws rather than returning.
        Should.Throw<ArgumentException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void Success_ConstructedWithAnError_Throws()
    {
        // The opposite half of the same invariant. Not reachable through the public
        // factories today; asserted so that a future factory cannot introduce the state.
        Should.Throw<ArgumentException>(() => new GuardProbe(isSuccess: true, _sampleError));
    }

    [Fact]
    public void GenericSuccess_ExposesItsValue()
    {
        Result<string> result = Result<string>.Success("kirana");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("kirana");
    }

    [Fact]
    public void GenericFailure_AccessingValue_Throws()
    {
        Result<string> result = Result<string>.Failure(_sampleError);

        InvalidOperationException exception =
            Should.Throw<InvalidOperationException>(() => result.Value);

        // The message names the error, so a caller that skipped its IsSuccess check
        // learns which failure it ignored rather than just seeing a null reference.
        exception.Message.ShouldContain(_sampleError.Code);
    }

    [Fact]
    public void GenericFailure_IsNotSuccessful()
    {
        Result<string> result = Result<string>.Failure(_sampleError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_sampleError);
    }

    [Fact]
    public void GenericFailure_ReturnsAGenericResult()
    {
        // Guards the `new` on Result<TValue>.Failure. Without it this call binds to the
        // inherited non-generic Result.Failure(Error) and quietly returns the wrong type,
        // which only shows up later as a compile error in an unrelated handler.
        Result<string> result = Result<string>.Failure(_sampleError);

        result.ShouldBeOfType<Result<string>>();
    }

    [Fact]
    public void NonGenericFactory_ProducesAGenericSuccess()
    {
        Result<int> result = Result.Success(42);

        result.Value.ShouldBe(42);
    }

    [Fact]
    public void NonGenericFactory_ProducesAGenericFailure()
    {
        Result<int> result = Result.Failure<int>(_sampleError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_sampleError);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        Result<string> result = "self-pickup";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("self-pickup");
    }

    /// <summary>
    /// Reaches the protected constructor so both halves of the success/error invariant can
    /// be exercised. <see cref="Result"/>'s public factories cannot produce a success that
    /// carries an error.
    /// </summary>
    private sealed class GuardProbe(bool isSuccess, Error error) : Result(isSuccess, error);
}
