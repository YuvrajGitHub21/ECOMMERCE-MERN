using System.Reflection;
using FluentValidation;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Application.Behaviors;
using GroceryEasy.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryEasy.Application;

/// <summary>
/// Wires up the Application layer: the dispatcher, every handler, every validator, and the
/// decorator pipeline they run inside.
/// </summary>
public static class DependencyInjection
{
    /// <summary>The Application assembly, used as the scan root.</summary>
    public static readonly Assembly Assembly = typeof(DependencyInjection).Assembly;

    /// <summary>Registers Application services.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDispatcher, Dispatcher>();

        // publicOnly: false because handlers are internal by design — an architecture test
        // enforces it — so a public-only scan would silently register nothing at all.
        services.Scan(scan => scan
            .FromAssemblies(Assembly)
                .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                    .AsImplementedInterfaces()
                    .WithScopedLifetime()
                .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                    .AsImplementedInterfaces()
                    .WithScopedLifetime()
                .AddClasses(c => c.AssignableTo(typeof(IValidator<>)), publicOnly: false)
                    .AsImplementedInterfaces()
                    .WithScopedLifetime());

        AddPipeline(services);

        return services;
    }

    /// <summary>
    /// Applies the decorator chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read this order backwards.</b> Each <c>Decorate</c> call wraps whatever is currently
    /// registered, so the <em>last</em> call becomes the <em>outermost</em> layer. Registering
    /// transaction, then validation, then logging produces:
    /// </para>
    /// <code>
    /// Logging( Validation( Transaction( Handler ) ) )
    /// </code>
    /// <para>
    /// Which is what is wanted, and each boundary earns its place. Logging outermost, so a
    /// request that fails validation still produces exactly one log line with a duration.
    /// Validation next, so an invalid command never opens a transaction — under the reverse
    /// order every rejected registration would take out a connection and a transaction to do
    /// nothing with. Transaction innermost, hugging the handler, so it spans the writes and
    /// nothing else.
    /// </para>
    /// <para>
    /// <c>TryDecorate</c> rather than <c>Decorate</c>: the latter throws when nothing matches,
    /// and there are legitimately zero handlers registered until the auth slice lands. It also
    /// keeps a command-free deployment from failing at boot for no reason.
    /// </para>
    /// </remarks>
    private static void AddPipeline(IServiceCollection services)
    {
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(CommandTransactionBehavior<,>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(CommandValidationBehavior<,>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(CommandLoggingBehavior<,>));

        // Queries get no transaction — see CommandTransactionBehavior.
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(QueryValidationBehavior<,>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(QueryLoggingBehavior<,>));
    }
}
