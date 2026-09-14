using EShop.Identity.IntegrationTests.Fixtures;

namespace EShop.Identity.IntegrationTests;

/// <summary>
/// Disposes the shared PostgreSQL container once the whole assembly has finished.
///
/// <para>
/// This lives in the <b>root</b> test namespace deliberately. A <c>[SetUpFixture]</c> applies to
/// its own namespace <i>and every namespace below it</i>, so one placed in
/// <c>EShop.Identity.IntegrationTests.Fixtures</c> alongside the container itself would never fire
/// for tests in sibling namespaces such as <c>...Persistence</c>. Placed here it covers every
/// fixture in the assembly.
/// </para>
///
/// <para>
/// It declares only a teardown — no <c>OneTimeSetUp</c> — so merely having it in the assembly does
/// not force the container to start. An InMemory-only run disposes nothing and never contacts
/// Docker. See <see cref="PostgresTestServer"/> for why startup is lazy.
/// </para>
/// </summary>
[SetUpFixture]
public class PostgresTestServerTeardown
{
    [OneTimeTearDown]
    public async Task StopContainerAsync() => await PostgresTestServer.DisposeAsync();
}
