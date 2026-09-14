using EShop.Identity.IntegrationTests.Fixtures;

namespace EShop.Identity.IntegrationTests;

/// <summary>
/// TEST-01. Base class for endpoint tests that must run against a real PostgreSQL database.
///
/// <para>
/// Inherit this <b>only</b> when the flow under test reaches provider-specific behaviour. In
/// practice that means anything touching <c>RefreshTokenRepository</c>'s two server-side UPDATEs —
/// refresh-token rotation, and password changes revoking every active session — plus anything
/// depending on column limits, unique indexes or the <c>Version</c> concurrency token. These
/// flows previously ran against an <c>IsInMemory()</c> fork that no longer exists, so InMemory
/// cannot execute them at all.
/// </para>
///
/// <para>
/// Everything else belongs on <see cref="IntegrationTestBase"/>. A Postgres fixture pays for a
/// cloned database per test method — not the migration chain, which the template database applies
/// once per run — so moving a fixture here that does not need it costs a <c>CREATE DATABASE</c>
/// rather than the ~1.7 s it once did.
/// </para>
/// </summary>
[Category("Postgres")]
public abstract class PostgresIntegrationTestBase : IntegrationTestBase
{
    protected override async Task<IdentityApiFactory> CreateFactoryAsync()
        => await PostgresIdentityApiFactory.CreateAsync();
}
