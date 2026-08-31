namespace GroceryEasy.Application.Abstractions.Messaging;

/// <summary>
/// The response type of a command that produces no value.
/// </summary>
/// <remarks>
/// Several Phase 1 commands — verify-email, logout, forgot-password, reset-password — succeed
/// without returning anything. The alternatives were a second non-generic
/// <c>ICommand</c>/<c>ICommandHandler</c>/<c>IDispatcher.Send</c> triple, which doubles the
/// dispatcher's wrapper machinery and every decorator in the pipeline, or abusing
/// <see cref="bool"/> as a response, which says nothing. A one-line empty type keeps the
/// pipeline to a single code path.
/// </remarks>
public readonly record struct Unit
{
    /// <summary>The only value this type has.</summary>
    public static readonly Unit Value;
}
