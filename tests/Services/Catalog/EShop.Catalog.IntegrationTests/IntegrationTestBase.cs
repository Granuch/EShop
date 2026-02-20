using EShop.Catalog.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests;

/// <summary>
/// Base class for all Catalog integration tests.
/// Provides common functionality and HTTP client.
/// </summary>
[Category("Integration")]
public abstract class IntegrationTestBase : IDisposable
{
    protected CatalogApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; set; } = null!;
    protected IServiceScope? ServiceScope { get; private set; }

    [SetUp]
    public virtual async Task SetUpAsync()
    {
        Factory = CreateFactory();
        Client = Factory.CreateClient();
        await Factory.InitializeDatabaseAsync();
    }

    [TearDown]
    public virtual async Task TearDownAsync()
    {
        ServiceScope?.Dispose();
        await Task.CompletedTask;
    }

    protected virtual CatalogApiFactory CreateFactory()
    {
        return new CatalogApiFactory();
    }

    protected IServiceScope CreateScope()
    {
        ServiceScope?.Dispose();
        ServiceScope = Factory.Services.CreateScope();
        return ServiceScope;
    }

    public void Dispose()
    {
        ServiceScope?.Dispose();
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
