using EShop.Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.IntegrationTests.Fixtures;

/// <summary>
/// TEST-01. A real <see cref="IdentityDbContext"/> on a real PostgreSQL database, with no web host
/// involved.
///
/// <para>
/// <b>Why not a <c>WebApplicationFactory</c>.</b> The obvious approach — subclass
/// <c>IdentityApiFactory</c> and swap the provider — does not work, and the reason is worth
/// recording: <c>Program.cs</c> hardcodes <c>var useInMemoryDb = builder.Environment.IsEnvironment("Testing")</c>
/// (`Program.cs:81`) with no configuration knob, so the production DI path always registers the
/// InMemory provider under the Testing environment. Registering Npgsql on top then fails with
/// <i>"Services for database providers 'Microsoft.EntityFrameworkCore.InMemory',
/// 'Npgsql.EntityFrameworkCore.PostgreSQL' have been registered... Only a single database provider
/// can be registered in a service provider"</i>, and stripping the <c>DbContextOptions</c>
/// descriptors first does not help, because <c>UseInMemoryDatabase</c> registers provider services
/// well beyond those. Making that work would mean adding a config switch to production
/// <c>Program.cs</c> purely for tests.
/// </para>
///
/// <para>
/// It is not needed here. Everything these tests cover — <c>ExecuteUpdateAsync</c>,
/// <c>ExecuteDeleteAsync</c>, column limits, unique indexes, concurrency tokens — is persistence
/// behaviour reachable from a <c>DbContext</c> alone. <c>IdentityDbContext</c> has an
/// options-only constructor, so this harness is a couple of lines and runs far faster than a host.
/// A test that genuinely needs the HTTP pipeline should use the InMemory
/// <c>IdentityApiFactory</c>, or Program.cs needs that config switch first.
/// </para>
/// </summary>
public sealed class PostgresDbHarness : IAsyncDisposable
{
    private readonly DbContextOptions<IdentityDbContext> _options;
    private readonly string _connectionString;

    private PostgresDbHarness(DbContextOptions<IdentityDbContext> options, string connectionString)
    {
        _options = options;
        _connectionString = connectionString;
    }

    /// <summary>
    /// Takes a fresh database from the shared container. The schema is already present — the
    /// database is cloned from a template that has had the migration chain applied — so there is
    /// nothing to create here.
    /// </summary>
    public static async Task<PostgresDbHarness> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PostgresDbHarness(options, connectionString);
    }

    /// <summary>
    /// A new context per call. Tests use separate contexts for arrange and assert so an assertion
    /// reads what the database actually holds rather than the change tracker's copy — which is the
    /// whole point when the code under test issues server-side UPDATEs the tracker never sees.
    /// </summary>
    public IdentityDbContext CreateContext() => new(_options);

    /// <summary>
    /// Releases this database's Npgsql pool — see <see cref="PostgresTestServer.ReleaseDatabase"/>
    /// for why leaving it to the GC exhausts the server's connection slots mid-run.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        PostgresTestServer.ReleaseDatabase(_connectionString);
        return ValueTask.CompletedTask;
    }
}
