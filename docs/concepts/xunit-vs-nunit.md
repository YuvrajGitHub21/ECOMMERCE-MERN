# xUnit v3 vs NUnit

## xUnit — what it is

A .NET test framework created by the original lead developer of NUnit v2 and a co-author, explicitly as a from-scratch rewrite of the things they'd concluded NUnit got wrong. The defining design choice: **a fresh instance of the test class is created for every single test method.**

```csharp
public class OrderPricingTests : IAsyncLifetime
{
    private readonly PricingEngine _sut = new();   // constructor = per-test setup

    [Fact]
    public void Applies_gst_correctly() { ... }

    [Theory]
    [InlineData(100, 18)]
    [InlineData(250, 5)]
    public void Rounds_away_from_zero(decimal price, decimal gstRate) { ... }

    public Task InitializeAsync() => ...;   // IAsyncLifetime = async per-test setup
    public Task DisposeAsync() => ...;      // async per-test teardown
}
```

- `[Fact]` — a single test.
- `[Theory]` + `[InlineData]`/`[MemberData]`/`[ClassData]` — one test method run against multiple data rows.
- Constructor + `IDisposable`/`IAsyncLifetime` — replaces `[SetUp]`/`[TearDown]`, scoped to one test method automatically, no attribute needed.
- `IClassFixture<T>` / `ICollectionFixture<T>` — the escape hatch for genuinely expensive shared setup (one Testcontainers PostgreSQL instance shared across an entire test collection, rather than per test).

## xUnit v3 specifically (2025)

- Its own **standalone test runner and execution engine**, rather than depending on the shared cross-framework runner infrastructure earlier versions used.
- Targets modern .NET only — v3 drops classic .NET Framework support; xUnit v2 remains the answer for projects still on .NET Framework.
- Improved parallelization control and richer diagnostics output.
- Aligned with Microsoft's newer `Microsoft.Testing.Platform`, alongside continued support for the classic VSTest adapter.

## NUnit — what it is

Older (originally a port of JUnit), attribute-heavy, and still extremely common, especially in codebases with roots going back further:

```csharp
[TestFixture]
public class OrderPricingTests
{
    private PricingEngine _sut;

    [SetUp]
    public void Setup() => _sut = new PricingEngine();   // runs before every test, shared fixture instance by default

    [Test]
    public void Applies_gst_correctly() { ... }

    [TestCase(100, 18)]
    [TestCase(250, 5)]
    public void Rounds_away_from_zero(decimal price, decimal gstRate) { ... }
}
```

`[SetUp]`/`[TearDown]` run before/after every test, but — unless explicitly configured otherwise — against a fixture instance NUnit may reuse across tests in the same class, which makes accidental shared mutable state easier to introduce than in xUnit's per-test-instance model. Assertions favor the constraint-style `Assert.That(x, Is.EqualTo(y))`. Very mature, huge install base, often the incumbent in older enterprise .NET codebases.

## The isolation difference is the one that actually matters day to day

xUnit's "new instance per test method" default means one test's leftover state **cannot** leak into the next test unless a developer deliberately opts into sharing it via a fixture. That property is what makes xUnit pair naturally with a Testcontainers-based integration suite: spin up one real PostgreSQL container per test *collection* (a deliberate, explicit fixture), reset the data between individual tests with [Respawn](https://github.com/jbogard/Respawn) — rather than fighting shared, implicitly-reused fixture state to get the same guarantee.

## In GroceryEasy

xUnit v3, with [Shouldly](https://github.com/shouldly/shouldly) for assertions — not FluentAssertions, which went commercial at v8 for the same reason [MediatR](mediatr.md) was dropped (see `C4` in [`docs/engineering-decisions.md`](../engineering-decisions.md)) — plus Testcontainers running real `postgres:17-alpine` and Respawn resetting state between tests (`G1`). The in-memory EF Core provider is explicitly avoided for integration tests because it doesn't enforce constraints or run real SQL, which would make passing tests actively misleading for a project whose entire point is proving correctness under concurrency and constraints.

## Interview questions

**Q: xUnit vs NUnit — what's the real, substantive difference?**
Test isolation model. xUnit creates a new instance of the test class per test method (constructor = setup, `IDisposable`/`IAsyncLifetime` = teardown), making accidental cross-test state leakage structurally harder. NUnit uses `[SetUp]`/`[TearDown]` attributes against a fixture instance that can be reused across tests, which is more familiar to developers coming from JUnit but easier to leak state through if you're not careful.

**Q: Why xUnit v3 specifically, not v2?**
v3 has its own standalone runner instead of depending on shared cross-framework runner infrastructure, better parallelization and diagnostics, and tracks Microsoft's newer testing platform going forward. v2 remains the right choice mainly for projects still targeting classic .NET Framework, which this isn't.

**Q: How does the test framework choice connect to using Testcontainers?**
xUnit's per-test-instance isolation model pairs naturally with `IClassFixture`/`ICollectionFixture` as the explicit, opt-in mechanism for expensive shared setup — spinning up one real Postgres container per test collection, then resetting data between individual tests with Respawn, rather than relying on implicit fixture reuse to avoid cross-test interference.
