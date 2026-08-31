using FluentValidation;
using GroceryEasy.Application;
using GroceryEasy.Application.Abstractions.Data;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroceryEasy.IntegrationTests.Messaging;

/// <summary>
/// Verifies the decorator pipeline as behaviour rather than as structure.
/// </summary>
/// <remarks>
/// Asserting the chain by reflecting over private fields would pass while testing nothing, and
/// would break on any refactor. What actually matters is observable: does an invalid command
/// reach the handler, and does it open a transaction on the way to being rejected. Those two
/// questions pin the ordering exactly.
/// </remarks>
public sealed class DispatcherPipelineTests
{
    [Fact]
    public async Task ValidCommand_ReachesTheHandler()
    {
        Harness harness = Harness.Create();

        Result<string> result = await harness.Dispatcher.Send(new ProbeCommand("kirana"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("handled kirana");
        harness.Handler.WasEntered.ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidCommand_NeverReachesTheHandler()
    {
        Harness harness = Harness.Create();

        Result<string> result = await harness.Dispatcher.Send(new ProbeCommand(string.Empty), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        harness.Handler.WasEntered.ShouldBeFalse();
    }

    [Fact]
    public async Task InvalidCommand_FailsAsValidationRatherThanThrowing()
    {
        Harness harness = Harness.Create();

        Result<string> result = await harness.Dispatcher.Send(new ProbeCommand(string.Empty), TestContext.Current.CancellationToken);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        var validationError = result.Error.ShouldBeOfType<ValidationError>();
        validationError.Errors.ShouldNotBeEmpty();
        validationError.Errors[0].Code.ShouldBe(nameof(ProbeCommand.Name));
    }

    [Fact]
    public async Task InvalidCommand_NeverOpensATransaction()
    {
        // This is the assertion that pins the ordering. If the transaction decorator sat
        // outside validation, every rejected request would take out a connection and a
        // transaction purely to roll it back again.
        Harness harness = Harness.Create();

        await harness.Dispatcher.Send(new ProbeCommand(string.Empty), TestContext.Current.CancellationToken);

        harness.DbContext.TransactionsStarted.ShouldBe(0);
    }

    [Fact]
    public async Task SucceedingCommand_CommitsExactlyOnce()
    {
        Harness harness = Harness.Create();

        await harness.Dispatcher.Send(new ProbeCommand("kirana"), TestContext.Current.CancellationToken);

        harness.DbContext.TransactionsStarted.ShouldBe(1);
        harness.DbContext.Commits.ShouldBe(1);
        harness.DbContext.Rollbacks.ShouldBe(0);
    }

    [Fact]
    public async Task FailingCommand_RollsBackInsteadOfCommitting()
    {
        // A handler that validates fine but decides the request cannot proceed — an email
        // already taken, say — must leave nothing behind.
        Harness harness = Harness.Create();
        harness.Handler.FailWith = Error.Conflict("Probe.Conflict", "Already exists.");

        Result<string> result = await harness.Dispatcher.Send(new ProbeCommand("kirana"), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        harness.DbContext.Commits.ShouldBe(0);
        harness.DbContext.Rollbacks.ShouldBe(1);
    }

    [Fact]
    public void LoggingIsTheOutermostDecorator()
    {
        // Named by string because the behaviours are internal to Application, which is the
        // point of them. Only the outermost layer is checked here; the two assertions above
        // establish that validation sits outside the transaction, and the transaction outside
        // the handler, which fixes the rest of the order.
        Harness harness = Harness.Create();

        ICommandHandler<ProbeCommand, string> resolved =
            harness.Provider.GetRequiredService<ICommandHandler<ProbeCommand, string>>();

        resolved.GetType().Name.ShouldStartWith("CommandLoggingBehavior");
    }

    [Fact]
    public async Task UnregisteredCommand_FailsLoudly()
    {
        // GetRequiredService, not GetService: a command with no handler is a wiring mistake,
        // and it should surface as an exception at the first call rather than a null.
        Harness harness = Harness.Create();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await harness.Dispatcher.Send(new UnhandledCommand(), TestContext.Current.CancellationToken));
    }

    private sealed record UnhandledCommand : ICommand<string>;

    private sealed class Harness
    {
        public required ServiceProvider Provider { get; init; }

        public required IDispatcher Dispatcher { get; init; }

        public required ProbeCommandHandler Handler { get; init; }

        public required RecordingDbContext DbContext { get; init; }

        public static Harness Create()
        {
            var handler = new ProbeCommandHandler();
            var dbContext = new RecordingDbContext();

            var services = new ServiceCollection();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton<IApplicationDbContext>(dbContext);

            // Registered before AddApplication so the TryDecorate calls have something to wrap.
            // Both are registered by hand because AddApplication's scan only sees Application's
            // own assembly, and these doubles live here in the test project.
            services.AddSingleton<ICommandHandler<ProbeCommand, string>>(handler);
            services.AddSingleton<IValidator<ProbeCommand>, ProbeCommandValidator>();

            services.AddApplication();

            ServiceProvider provider = services.BuildServiceProvider();

            return new Harness
            {
                Provider = provider,
                Dispatcher = provider.GetRequiredService<IDispatcher>(),
                Handler = handler,
                DbContext = dbContext,
            };
        }
    }
}
