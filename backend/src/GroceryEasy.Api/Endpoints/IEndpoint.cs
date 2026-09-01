using System.Reflection;

namespace GroceryEasy.Api.Endpoints;

/// <summary>
/// One HTTP endpoint, or one small group of closely related ones.
/// </summary>
/// <remarks>
/// <para>
/// Minimal APIs with a convention, not MVC controllers. A controller gathers a dozen unrelated
/// actions behind one class because they happen to share a route prefix, and its filters,
/// model-binding and action-selection machinery are cost paid on every request for behaviour this
/// API does not use. An endpoint here is a file, and the file is the whole story: route, request
/// shape, authorization, and the handler it dispatches to. See ADR-0014.
/// </para>
/// <para>
/// Discovered by assembly scan, so adding an endpoint is adding a file — never also remembering
/// to register it somewhere else. Registration you can forget is registration that gets forgotten.
/// </para>
/// </remarks>
public interface IEndpoint
{
    /// <summary>Maps this endpoint onto the route builder.</summary>
    void MapEndpoint(IEndpointRouteBuilder app);
}

/// <summary>Discovery and registration for <see cref="IEndpoint"/>.</summary>
public static class EndpointExtensions
{
    /// <summary>Registers every <see cref="IEndpoint"/> in the assembly as a service.</summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        ServiceDescriptor[] descriptors = [.. assembly
            .DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && type.IsAssignableTo(typeof(IEndpoint)))
            .Select(type => ServiceDescriptor.Transient(typeof(IEndpoint), type))];

        services.TryAddEnumerableRange(descriptors);

        return services;
    }

    /// <summary>Maps every registered endpoint.</summary>
    public static IApplicationBuilder MapEndpoints(this WebApplication app, RouteGroupBuilder? group = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        IEndpointRouteBuilder builder = group is null ? app : group;

        foreach (IEndpoint endpoint in app.Services.GetRequiredService<IEnumerable<IEndpoint>>())
        {
            endpoint.MapEndpoint(builder);
        }

        return app;
    }

    private static void TryAddEnumerableRange(
        this IServiceCollection services,
        IEnumerable<ServiceDescriptor> descriptors)
    {
        foreach (ServiceDescriptor descriptor in descriptors)
        {
            services.Add(descriptor);
        }
    }
}
