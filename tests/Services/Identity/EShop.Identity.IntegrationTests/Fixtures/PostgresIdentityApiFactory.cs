using EShop.Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Fixtures;

/// <summary>
/// TEST-01. The full Identity HTTP host, backed by a real PostgreSQL database.
///
/// <para>
/// Use this for endpoint tests whose behaviour depends on the provider — anything reaching
/// <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c>, column limits, unique indexes or the
/// <c>Version</c> concurrency token. Refresh-token rotation and "changing a password revokes every
/// session" are the two flows that need it, because both go through
/// <c>RefreshTokenRepository</c>'s server-side UPDATE. Everything else should stay on the InMemory
/// <see cref="IdentityApiFactory"/>, which is far faster.
/// </para>
///
/// <para>
/// <b>Two things have to line up for this to work.</b> First, <c>Program.cs</c> picks the provider
/// from <c>Testing:UseRelationalDatabase</c>; without that flag the Testing environment hardcodes
/// InMemory and registering Npgsql on top throws <i>"Only a single database provider can be
/// registered in a service provider"</i>. Second, both settings must be delivered through
/// <c>UseSetting</c> — host configuration — because <c>Program.cs</c> reads them while composing
/// the app, and a <c>ConfigureAppConfiguration</c> source is only applied when the host is finally
/// built, i.e. too late, silently leaving the defaults in place.
/// </para>
///
/// <para>
/// Because the relational branch is now active, this host also runs the real migration chain at
/// startup rather than <c>EnsureCreated</c> — so these tests additionally prove the migrations
/// apply cleanly to an empty database.
/// </para>
/// </summary>
public class PostgresIdentityApiFactory : IdentityApiFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Protected so specialised factories (e.g. <c>RateLimitingApiFactory</c>) can add host
    /// settings while keeping the relational provider. Obtain the connection string from
    /// <see cref="PostgresTestServer.CreateDatabaseAsync"/> first.
    /// </summary>
    protected PostgresIdentityApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Async because the database must exist before the host is built. Each factory gets its own
    /// database, so fixtures cannot see each other's rows.
    /// </summary>
    public static async Task<PostgresIdentityApiFactory> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync(cancellationToken);
        return new PostgresIdentityApiFactory(connectionString);
    }

    /// <summary>
    /// Builds a factory against an existing database. Used by
    /// <see cref="PostgresTestServer"/> to seed the template — it must not go through
    /// <see cref="CreateAsync"/>, which would re-enter the template-creation lock it is already
    /// holding.
    /// </summary>
    internal static PostgresIdentityApiFactory ForConnectionString(string connectionString)
        => new(connectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Testing:UseRelationalDatabase", "true");
        builder.UseSetting("ConnectionStrings:IdentityDb", _connectionString);

        base.ConfigureWebHost(builder);
    }

    /// <summary>
    /// Replaces the base factory's InMemory registration. Both this and the production path
    /// register Npgsql, so there is only ever one provider in the container.
    /// </summary>
    protected override void ConfigureDatabase(IServiceCollection services)
    {
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(_connectionString));
    }

    /// <summary>
    /// Releases this database's Npgsql pool. Each test uses a distinct connection string and Npgsql
    /// pools per connection string, so without this the run accumulates pools until the server
    /// answers <c>53300: sorry, too many clients already</c> — which shows up as a wave of
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
