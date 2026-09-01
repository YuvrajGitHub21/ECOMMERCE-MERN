using System.Reflection;
using GroceryEasy.Application.Abstractions.Messaging;
using GroceryEasy.Domain.Common;
using NetArchTest.Rules;

// Both xUnit and NetArchTest define a TestResult. The alias keeps the architecture-rule one
// unambiguous without dropping the xUnit global using every other test file relies on.
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace GroceryEasy.ArchitectureTests;

/// <summary>
/// The layering rules, as executable assertions rather than documentation.
/// </summary>
/// <remarks>
/// <para>
/// A diagram in a README describes the architecture someone intended. These tests describe the
/// one that exists, and they fail the build when the two diverge. That is the whole argument for
/// fitness functions: an architectural rule nothing enforces is a rule that decays, quietly,
/// starting with the first person in a hurry.
/// </para>
/// <para>
/// Kept deliberately few and high-signal. A suite that asserts thirty conventions is a suite
/// people start deleting rules from.
/// </para>
/// </remarks>
public sealed class LayeringTests
{
    private static readonly Assembly _domain = typeof(Result).Assembly;
    private static readonly Assembly _application = typeof(ICommand<>).Assembly;
    private static readonly Assembly _infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly _api = typeof(Api.Endpoints.IEndpoint).Assembly;

    /// <summary>
    /// Domain depends on nothing but the base class library.
    /// </summary>
    /// <remarks>
    /// This is the rule the whole structure rests on. Domain holds the pricing engine and the
    /// order state machine — the parts that must be testable with no database, no clock and no
    /// container. The moment it can reach Entity Framework, someone will make it, and those tests
    /// stop being cheap.
    /// </remarks>
    [Fact]
    public void Domain_DependsOnNothingButTheBaseClassLibrary()
    {
        ArchTestResult result = Types.InAssembly(_domain)
            .Should()
            .NotHaveDependencyOnAny(
                "GroceryEasy.Application",
                "GroceryEasy.Infrastructure",
                "GroceryEasy.Api",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }

    /// <summary>Domain has zero package references, enforced rather than hoped for.</summary>
    [Fact]
    public void Domain_HasNoPackageReferences()
    {
        string[] referenced = [.. _domain
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                && !string.Equals(name, "netstandard", StringComparison.Ordinal))];

        referenced.ShouldBeEmpty();
    }

    /// <summary>
    /// Application knows there is a database, but not which one.
    /// </summary>
    /// <remarks>
    /// Note what is <b>not</b> asserted: that Application avoids
    /// <c>Microsoft.EntityFrameworkCore</c>. It references it deliberately, for
    /// <c>DbSet&lt;T&gt;</c> and the transaction handle on <c>IApplicationDbContext</c> — see the
    /// no-generic-repository decision in ADR-0006. The boundary that actually matters is the
    /// <em>provider</em>: nothing in Application may know the store is PostgreSQL.
    /// </remarks>
    [Fact]
    public void Application_DoesNotDependOnTheDatabaseProviderOrOnInfrastructure()
    {
        ArchTestResult result = Types.InAssembly(_application)
            .Should()
            .NotHaveDependencyOnAny(
                "GroceryEasy.Infrastructure",
                "GroceryEasy.Api",
                "Npgsql",
                "Microsoft.EntityFrameworkCore.Design")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }

    /// <summary>
    /// Handlers are sealed and internal.
    /// </summary>
    /// <remarks>
    /// Internal because nothing outside Application should ever call a handler directly — the
    /// dispatcher is the only door, and it is what applies validation, logging and the
    /// transaction. A public handler is an invitation to bypass all three. Sealed because a
    /// handler is a leaf: behaviour is composed with decorators, never with inheritance.
    /// </remarks>
    [Fact]
    public void Handlers_AreSealedAndInternal()
    {
        ArchTestResult result = Types.InAssembly(_application)
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .BeSealed()
            .And()
            .NotBePublic()
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }

    /// <summary>Endpoints are sealed — they are leaves, and an endpoint hierarchy helps nobody.</summary>
    [Fact]
    public void Endpoints_AreSealed()
    {
        ArchTestResult result = Types.InAssembly(_api)
            .That()
            .ImplementInterface(typeof(Api.Endpoints.IEndpoint))
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
        result.IsSuccessful.ShouldBeTrue();
    }

    /// <summary>
    /// Infrastructure is reachable only through its registration entry point.
    /// </summary>
    /// <remarks>
    /// A guard against the API quietly newing up a concrete Infrastructure service instead of
    /// depending on the Application abstraction. Everything in Infrastructure is internal except
    /// the deliberate seams — <c>DependencyInjection</c>, the options classes, the health check,
    /// the role seeder and the Identity entities the database context is generic over.
    /// </remarks>
    [Fact]
    public void Infrastructure_ExposesOnlyItsDeliberateSeams()
    {
        // Migrations are excluded: Entity Framework's generator emits them as public classes
        // and there is no supported way to change that. Listing each one here instead would mean
        // this test breaks on every `migrations add`, which is how a useful rule becomes one
        // people edit reflexively without reading.
        string[] publicTypes = [.. _infrastructure
            .GetExportedTypes()
            .Where(type => type.Namespace?.Contains(".Migrations", StringComparison.Ordinal) != true)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)];

        string[] expected =
        [
            "ApplicationDbContext",
            "ApplicationRole",
            "ApplicationUser",
            "DatabaseHealthCheck",
            "DependencyInjection",
            "EmailOptions",
            "FrontendOptions",
            "JwtOptions",
            "RefreshToken",
            "RoleSeeder",
            "Roles",
        ];

        publicTypes.ShouldBe(expected);
    }
}
