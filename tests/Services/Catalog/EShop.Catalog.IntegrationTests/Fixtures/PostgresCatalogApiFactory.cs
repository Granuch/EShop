using EShop.Catalog.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// Stage 0 / TEST-01. The full Catalog HTTP host, backed by a real PostgreSQL database.
///
/// <para>
/// This is the default for <see cref="IntegrationTestBase"/>, not an opt-in for a handful of
/// fixtures. Too much of Catalog is unreachable on InMemory to keep it as the baseline: search goes
/// through <c>EF.Functions.ILike</c>, "at most one main image" is a partial unique index rather than
/// application logic, and SKU uniqueness is supposed to be an index. Keeping InMemory would mean
/// continuing to test something the service does not do.
/// </para>
///
/// <para>
/// <b>Two things have to line up for this to work.</b> First, <c>Program.cs</c> picks the provider
/// from <c>Testing:UseRelationalDatabase</c>; without that flag the Testing environment hardcodes
/// InMemory and registering Npgsql on top throws <i>"Only a single database provider can be
/// registered in a service provider"</i> — and stripping the <c>DbContextOptions</c> descriptors
/// first does not help, because <c>UseInMemoryDatabase</c> registers provider services well beyond
/// those. Second, both settings must arrive through <c>UseSetting</c> — host configuration —
/// because <c>Program.cs</c> reads them while composing the app, and a
/// <c>ConfigureAppConfiguration</c> source is only applied when the host is finally built, i.e. too
/// late, silently leaving the defaults in place.
/// </para>
///
/// <para>
/// Because the relational branch is active, this host also runs Catalog's real startup migration
/// loop instead of <c>EnsureCreated</c> — so these tests additionally prove the migration chain
/// applies cleanly. The database itself is cloned from an already-migrated template, so that loop
/// finds nothing pending and costs a round trip.
/// </para>
/// </summary>
public class PostgresCatalogApiFactory : CatalogApiFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Protected so specialised factories can add host settings while keeping the relational
    /// provider. Obtain the connection string from
    /// <see cref="PostgresTestServer.CreateDatabaseAsync"/> first.
    /// </summary>
    protected PostgresCatalogApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Async because the database must exist before the host is built. Each factory gets its own
    /// database, so fixtures cannot see each other's rows.
    /// </summary>
    public static async Task<PostgresCatalogApiFactory> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync(cancellationToken);
        return new PostgresCatalogApiFactory(connectionString);
    }

    /// <summary>
    /// Builds a factory against an existing database. Used by <see cref="PostgresTestServer"/> to
    /// seed the template — it must not go through <see cref="CreateAsync"/>, which would re-enter
    /// the template-creation lock it is already holding.
    /// </summary>
    internal static PostgresCatalogApiFactory ForConnectionString(string connectionString)
        => new(connectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Testing:UseRelationalDatabase", "true");
        builder.UseSetting("ConnectionStrings:CatalogDb", _connectionString);

        base.ConfigureWebHost(builder);
    }

    /// <summary>
    /// Replaces the base factory's InMemory registration. Both this and the production path register
    /// Npgsql, so there is only ever one provider in the container.
    /// </summary>
    protected override void ConfigureDatabase(IServiceCollection services)
    {
        services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(_connectionString));
    }

    /// <summary>
    /// No-op: the schema comes from the migrated template, and the host's own startup migration
    /// loop has already run by the time this is called. Calling <c>EnsureCreated</c> on a migrated
    /// relational database is at best a wasted round trip and at worst creates a schema that no
    /// migration produced.
    /// </summary>
    protected override Task EnsureSchemaAsync(CatalogDbContext db) => Task.CompletedTask;

    /// <summary>
    /// Releases this database's Npgsql pool. Each fixture uses a distinct connection string and
    /// Npgsql pools per connection string, so without this the run accumulates pools until the
    /// server answers <c>53300: sorry, too many clients already</c> — which shows up as a wave of
    /// unrelated-looking SetUp failures rather than anything pointing at pooling.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            PostgresTestServer.ReleaseDatabase(_connectionString);
        }
    }
}
