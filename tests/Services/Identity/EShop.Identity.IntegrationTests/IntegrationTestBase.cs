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

    [SetUp]
    public virtual async Task SetUpAsync()
    {
        Factory = await CreateFactoryAsync();
        Client = Factory.CreateClient();
        await Factory.InitializeDatabaseAsync();
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
        await Task.CompletedTask;
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

    public void Dispose()
    {
        ServiceScope?.Dispose();
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
