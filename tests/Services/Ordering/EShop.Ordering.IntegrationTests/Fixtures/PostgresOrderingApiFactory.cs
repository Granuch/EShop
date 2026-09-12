using EShop.Ordering.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Fixtures;

/// <summary>
/// Ordering audit M11. The full Ordering host on a real PostgreSQL database — the default for
/// <see cref="IntegrationTestBase"/>, not an opt-in.
///
/// <para>
/// Both settings go through <c>UseSetting</c> (host configuration): <c>Program.cs</c> reads them while
/// composing the app, and a <c>ConfigureAppConfiguration</c> source arrives too late, silently leaving
/// InMemory in place. <c>Testing:UseRelationalDatabase</c> is what stops Program.cs registering InMemory
/// at all — registering Npgsql on top of it throws "Only a single database provider can be registered".
/// With it set, the host also runs its real startup migration loop and the Npgsql health check.
/// </para>
/// </summary>
public class PostgresOrderingApiFactory : OrderingApiFactory
{
    private readonly string _connectionString;

    protected PostgresOrderingApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Async because the database must exist before the host is built.</summary>
    public static async Task<PostgresOrderingApiFactory> CreateAsync(CancellationToken cancellationToken = default)
        => new(await PostgresTestServer.CreateDatabaseAsync(cancellationToken));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Testing:UseRelationalDatabase", "true");
        builder.UseSetting("ConnectionStrings:OrderingDb", _connectionString);

        base.ConfigureWebHost(builder);
    }

    protected override void ConfigureDatabase(IServiceCollection services)
    {
        services.AddDbContext<OrderingDbContext>(options =>
        {
            options.UseNpgsql(_connectionString);
            ConfigureNpgsql(options);
        });
    }

    /// <summary>Lets a specialised factory add to the context options, e.g. a command interceptor.</summary>
    protected virtual void ConfigureNpgsql(DbContextOptionsBuilder options)
    {
    }

    /// <summary>No-op: the schema comes from the migrated template, and the host's startup migration has run.</summary>
    protected override Task EnsureSchemaAsync(OrderingDbContext db) => Task.CompletedTask;

    /// <summary>Releases this database's Npgsql pool; without it pools accumulate until <c>53300</c>.</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            PostgresTestServer.ReleaseDatabase(_connectionString);
        }
    }
}
