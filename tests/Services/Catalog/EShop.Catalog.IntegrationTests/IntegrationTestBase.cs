using EShop.Catalog.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests;

/// <summary>
/// Base class for all Catalog integration tests.
/// Provides common functionality and HTTP client.
///
/// <para>
/// <b>These run against a real PostgreSQL container (Testcontainers), not EF InMemory.</b> That
/// changed in Stage 0 and it was not cosmetic: on InMemory no test could pass a <c>SearchTerm</c>
/// (<c>ProductQueryService</c> uses <c>EF.Functions.ILike</c>, which InMemory cannot translate), no
/// unique or GIN index existed, and the partial unique index that
/// <c>SetMainProductImageCommandHandler</c>'s two-save demotion exists to satisfy was not enforced.
/// The missing unique index on <c>Products.Sku</c> survived for exactly that reason.
/// </para>
///
/// <para>
/// Practical consequences: <b>this suite now needs Docker</b>, and the container starts once per
/// run, lazily, via <see cref="PostgresTestServer"/>. Each fixture gets its own database cloned from
/// a migrated, seeded template.
/// </para>
/// </summary>
[Category("Integration")]
public abstract class IntegrationTestBase : IDisposable
{
    protected CatalogApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; set; } = null!;
    protected IServiceScope? ServiceScope { get; private set; }

    /// <summary>
    /// PERF-02. Whether the host and its database are built once per fixture (default) or once per
    /// test method.
    ///
    /// <para>
    /// Per-fixture also fixes a leak this class shipped with: NUnit reuses one fixture instance
    /// across its test methods, so a <c>[SetUp]</c> that reassigned <c>Factory</c> dropped every
    /// previous factory with its host still running, and <c>Dispose()</c> — which runs once — only
    /// ever reached the last one. Under Postgres that also means the database and its Npgsql pool
    /// were never released.
    /// </para>
    ///
    /// <para>
    /// <b>Override to false when a test needs host state its neighbours cannot have touched.</b> In
    /// practice that means rate limiting, whose buckets live in the host — though Catalog disables
    /// both limiters under <c>ASPNETCORE_ENVIRONMENT=Testing</c>, so no fixture needs it today.
    /// Sharing the <i>database</i> is the other half of the trade: tests in a fixture see each
    /// other's rows, so create per-test data under a unique key (<c>GenerateUniqueSku</c>, a
    /// GUID-suffixed slug) rather than relying on the seeded rows being the only ones present.
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

        // Disposing the factory is what calls PostgresTestServer.ReleaseDatabase, so skipping it
        // leaks the database and its connection pool for the rest of the run.
        Factory?.Dispose();
        Factory = null!;
    }

    /// <summary>
    /// Builds the factory. Async because the Postgres factory must create its database before the
    /// host is built. Override to supply a specialised factory.
    /// </summary>
    protected virtual async Task<CatalogApiFactory> CreateFactoryAsync()
        => await PostgresCatalogApiFactory.CreateAsync();

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

    /// <summary>
    /// Safety net only — <see cref="OneTimeTearDown"/> and <see cref="TearDownAsync"/> already
    /// release the host on both paths, and both null the fields so this cannot double-dispose.
    /// </summary>
    public void Dispose()
    {
        ServiceScope?.Dispose();
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
