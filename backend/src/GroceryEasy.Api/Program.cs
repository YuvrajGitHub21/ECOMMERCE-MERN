var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

await app.RunAsync();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> in the integration tests has a
/// type to hang the test host off. Top-level statements generate this class as
/// <c>internal</c>, which the factory cannot reach; redeclaring it here as public is the
/// documented workaround and is cheaper than an <c>InternalsVisibleTo</c>.
/// </summary>
public partial class Program;
