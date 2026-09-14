using EShop.Ordering.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests;

/// <summary>
/// Base class for all Ordering integration tests.
///
/// <para>
/// <b>These run against a real PostgreSQL container (Testcontainers), not EF InMemory</b> — Ordering
/// audit M11, the same port Catalog made in its Stage 0. The suite therefore needs Docker; the container
/// starts once per run, lazily, and each fixture gets its own database cloned from a migrated template
/// (<see cref="PostgresTestServer"/>).
/// </para>
/// </summary>
[Category("Integration")]
public abstract class IntegrationTestBase : IDisposable
{
    protected OrderingApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; set; } = null!;
    protected IServiceScope? ServiceScope { get; private set; }

    /// <summary>
    /// Whether the host and its database are built once per fixture (default) or once per test.
    ///
    /// <para>
    /// Per-fixture also fixes a leak this class had: NUnit reuses one fixture instance for all its
    /// tests, so a <c>[SetUp]</c> that reassigned <c>Factory</c> dropped every earlier factory with its
    /// host still running, and <c>Dispose()</c>, which runs once, only reached the last. On Postgres that
    /// would also leak each database's connection pool.
    /// </para>
    /// <para>
    /// The trade is that a fixture's tests share one database and see each other's rows: create per-test
    /// data under a unique key rather than assuming the seeded order is the only one. Override to false
    /// for a fixture that needs a pristine host.
    /// </para>
    /// </summary>
    protected virtual bool UseFixtureScopedHost => true;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        if (UseFixtureScopedHost)
        {
            await BuildHostAsync();
        }
    }

    [SetUp]
    public virtual async Task SetUpAsync()
    {
        if (!UseFixtureScopedHost)
        {
            await BuildHostAsync();
        }
    }

    private async Task BuildHostAsync()
    {
        Factory = await CreateFactoryAsync();
        Client = Factory.CreateClient();
        await Factory.InitializeDatabaseAsync();
    }

    private void DisposeHost()
    {
        Client?.Dispose();
        Client = null!;

        // Disposing the factory is what releases the database's connection pool.
        Factory?.Dispose();
        Factory = null!;
    }

    /// <summary>Builds the factory; async because the database must exist before the host is built.</summary>
    protected virtual async Task<OrderingApiFactory> CreateFactoryAsync()
        => await PostgresOrderingApiFactory.CreateAsync();

    [TearDown]
    public virtual async Task TearDownAsync()
    {
        ServiceScope?.Dispose();
        ServiceScope = null;

        if (!UseFixtureScopedHost)
        {
            DisposeHost();
        }

        await Task.CompletedTask;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (UseFixtureScopedHost)
        {
            DisposeHost();
        }
    }

    protected IServiceScope CreateScope()
    {
        ServiceScope?.Dispose();
        ServiceScope = Factory.Services.CreateScope();
        return ServiceScope;
    }

    /// <summary>Safety net only; both teardown paths already release the host and null the fields.</summary>
    public void Dispose()
    {
        ServiceScope?.Dispose();
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
