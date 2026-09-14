using EShop.Ordering.IntegrationTests.Fixtures;

namespace EShop.Ordering.IntegrationTests;

/// <summary>
/// Disposes the shared PostgreSQL container once the whole assembly has finished.
///
/// <para>
/// It lives in the <b>root</b> test namespace deliberately: a <c>[SetUpFixture]</c> covers its own
/// namespace and those below it, so one placed in <c>…IntegrationTests.Fixtures</c> would never fire for
/// tests in <c>…Orders</c>, <c>…Security</c> and the rest. It declares only a teardown, so its presence
/// does not start the container.
/// </para>
/// </summary>
[SetUpFixture]
public class PostgresTestServerTeardown
{
    [OneTimeTearDown]
    public async Task StopContainerAsync() => await PostgresTestServer.DisposeAsync();
}
