using EShop.Payment.IntegrationTests.Fixtures;

namespace EShop.Payment.IntegrationTests;

/// <summary>
/// Disposes the shared PostgreSQL container once the whole assembly has finished.
///
/// <para>
/// It lives in the <b>root</b> test namespace deliberately: a <c>[SetUpFixture]</c> covers its own namespace
/// and those below it, so one placed in <c>…IntegrationTests.Fixtures</c> would never fire for the tests in
/// <c>…Persistence</c>. It declares only a teardown, so its presence does not start the container.
/// </para>
/// </summary>
[SetUpFixture]
public class PostgresTestServerTeardown
{
    [OneTimeTearDown]
    public async Task StopContainerAsync() => await PostgresTestServer.DisposeAsync();
}
