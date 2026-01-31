using EShop.Identity.IntegrationTests.Fixtures;

namespace EShop.Identity.IntegrationTests;

/// <summary>
/// Base class for all Integration tests
/// Provides common functionality and HTTP client
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected IdentityApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public virtual async Task SetUpAsync()
    {
        Factory = new IdentityApiFactory();
        Client = Factory.CreateClient();
        await Factory.InitializeDatabaseAsync();
    }

    [TearDown]
    public virtual void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        Client?.Dispose();
        Factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
