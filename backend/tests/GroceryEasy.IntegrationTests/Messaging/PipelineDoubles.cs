using FluentValidation;
using GroceryEasy.Application.Abstractions.Data;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace GroceryEasy.IntegrationTests.Messaging;

/// <summary>A command that exists only to be pushed through the pipeline.</summary>
internal sealed record ProbeCommand(string Name) : ICommand<string>;

/// <summary>
/// Records whether it ran and what it was told to return.
/// </summary>
/// <remarks>
/// Registered directly by the tests rather than discovered by the assembly scan, because the
/// scan only looks inside the Application assembly. Registering it before
/// <c>AddApplication()</c> is what lets <c>TryDecorate</c> wrap it.
/// </remarks>
internal sealed class ProbeCommandHandler : ICommandHandler<ProbeCommand, string>
{
    public bool WasEntered { get; private set; }

    public Error? FailWith { get; set; }

    public Task<Result<string>> Handle(ProbeCommand command, CancellationToken ct)
    {
        WasEntered = true;

        return Task.FromResult(FailWith is null
            ? Result<string>.Success($"handled {command.Name}")
            : Result<string>.Failure(FailWith));
    }
}

/// <summary>Rejects an empty name, so a request can be made invalid on demand.</summary>
internal sealed class ProbeCommandValidator : AbstractValidator<ProbeCommand>
{
    public ProbeCommandValidator() =>
        RuleFor(command => command.Name)
            .NotEmpty()
            .WithMessage("Name must not be empty.");
}

/// <summary>
/// An <see cref="IApplicationDbContext"/> that opens no connection and simply records what the
/// transaction decorator asked it to do.
/// </summary>
internal sealed class RecordingDbContext : IApplicationDbContext
{
    public int TransactionsStarted { get; private set; }

    public int Commits { get; private set; }

    public int Rollbacks { get; private set; }

    public bool HasActiveTransaction { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        TransactionsStarted++;
        HasActiveTransaction = true;
        return Task.FromResult<IDbContextTransaction>(new RecordingTransaction(this));
    }

    private sealed class RecordingTransaction(RecordingDbContext owner) : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.CreateVersion7();

        public void Commit()
        {
            owner.Commits++;
            owner.HasActiveTransaction = false;
        }

        public void Rollback()
        {
            owner.Rollbacks++;
            owner.HasActiveTransaction = false;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Commit();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            Rollback();
            return Task.CompletedTask;
        }

        public void Dispose() => owner.HasActiveTransaction = false;

        public ValueTask DisposeAsync()
        {
            owner.HasActiveTransaction = false;
            return ValueTask.CompletedTask;
        }
    }
}
