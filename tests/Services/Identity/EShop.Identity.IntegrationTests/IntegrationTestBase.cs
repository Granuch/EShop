using EShop.Identity.IntegrationTests.Fixtures;
using EShop.Identity.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests;

/// <summary>
/// Base class for all Integration tests. Provides common functionality and an HTTP client.
///
/// <para>
/// <b>These run against a real PostgreSQL container (Testcontainers), not EF InMemory.</b> That
/// changed in Stage 8 and it was not optional: once the <c>IsInMemory()</c> forks came out of
/// <c>RefreshTokenRepository</c> and <c>TokenCleanupService</c>, and login's last-login write moved
/// to <c>IUserRepository.UpdateLastLoginAsync</c>, the production code paths use
/// <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c> — which the InMemory provider cannot
/// execute at all. Keeping InMemory would have meant keeping the forks, i.e. continuing to ship
/// one query and test another.
/// </para>
///
/// <para>
/// Practical consequences: <b>this suite now needs Docker</b>, and the container is started once
/// per run, lazily, by <see cref="PostgresTestServer"/>. Each test gets its own database and the
/// full migration chain is applied to it, so the suite also proves the migrations apply cleanly.
/// </para>
/// </summary>
[Category("Integration")]
public abstract class IntegrationTestBase : IDisposable
{
    protected IdentityApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; set; } = null!;
    protected IServiceScope? ServiceScope { get; private set; }

    /// <summary>
    /// PERF-02. Whether the host and its database are built once per fixture (default) or once per
    /// test method.
    ///
    /// <para>
    /// Per-fixture is right for almost everything: a host costs a `CREATE DATABASE … TEMPLATE`
    /// plus a host build plus a seed check, and paying that 169 times rather than ~25 was roughly
    /// a third of this suite's runtime. It also fixes a leak — NUnit reuses one fixture instance
    /// across its test methods, so a `[SetUp]` that reassigned the factory dropped every previous
    /// one undisposed, and since disposal is what returns the database and its Npgsql pool, those
    /// were held for the rest of the run (measured on <c>ProfileTests</c>: 13 created, 5 released).
    /// </para>
    ///
    /// <para>
    /// <b>Override to false when a test needs host state that its neighbours cannot have touched.</b>
    /// In practice that means rate limiting — the limiter's buckets live in the host, so a shared
    /// host lets one test spend another's allowance and the fixture becomes order-dependent.
    /// Sharing the <i>database</i> is the other half of this trade: tests in a fixture now see each
    /// other's rows, so create per-test data under a unique key rather than mutating the seeded
    /// users.
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
    /// host is built. Override to supply a specialised factory — see <c>RateLimitingApiFactory</c>,
    /// which adds host settings on top of the same relational provider.
    /// </summary>
    protected virtual async Task<IdentityApiFactory> CreateFactoryAsync()
        => await PostgresIdentityApiFactory.CreateAsync();

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

    protected async Task<string> CreateTestUserAsync(
        string? email = null,
        string? password = null,
        string role = TestUsers.Roles.User)
    {
        using var scope = Factory.Services.CreateScope();
        return await UserManagementHelper.CreateTestUserAsync(
            scope.ServiceProvider, 
            email, 
            password, 
            role);
    }

    protected async Task DeleteTestUserAsync(string userId)
    {
        using var scope = Factory.Services.CreateScope();
        await UserManagementHelper.DeleteTestUserAsync(scope.ServiceProvider, userId);
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
