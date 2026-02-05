using EShop.Identity.IntegrationTests.Fixtures;

namespace EShop.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Shared database fixture for test classes
/// Ensures database is created once per test class instead of per test
/// </summary>
[SetUpFixture]
public class SharedDatabaseFixture
{
    public static IdentityApiFactory? SharedFactory { get; private set; }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        SharedFactory = new IdentityApiFactory(useSharedDatabase: true);
        await SharedFactory.InitializeDatabaseAsync();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        SharedFactory?.Dispose();
        SharedFactory = null;
    }
}
