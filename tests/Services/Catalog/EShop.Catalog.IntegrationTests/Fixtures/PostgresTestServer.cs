using EShop.Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// Stage 0 / TEST-01, ported from Identity. Owns the single PostgreSQL container shared by every
/// test in this assembly and hands out a fresh database per caller — which, since the host is
/// fixture-scoped, means one per <i>fixture</i> rather than one per test method. See
/// <c>IntegrationTestBase.UseFixtureScopedHost</c>.
///
/// <para>
/// <b>Why Catalog is on Postgres at all.</b> The whole suite ran on EF InMemory, and a great deal of
/// this service is invisible from there: <c>ProductQueryService</c> searches with
/// <c>EF.Functions.ILike</c> (InMemory cannot translate it, so <b>no test could pass a
/// <c>SearchTerm</c> at all</b>), unique and GIN indexes do not exist, the partial unique index that
/// <c>SetMainProductImageCommandHandler</c>'s two-save demotion exists to satisfy is not enforced,
/// and neither are <c>decimal(18,2)</c> precision or the column length caps. That is not a
/// theoretical gap — the missing unique index on <c>Products.Sku</c> survived precisely because no
/// test could observe it.
/// </para>
///
/// <para>
/// <b>Two things here are not incidental, and removing either breaks the suite.</b>
/// </para>
///
/// <para>
/// (1) <b>The template database.</b> Applying the full migration chain per database is slow enough
/// to dominate the run. Migrations are applied once to a template, and each database is produced
/// with <c>CREATE DATABASE … TEMPLATE</c>, which Postgres does as a file copy. The copy carries
/// <c>__EFMigrationsHistory</c>, so the host's own startup <c>MigrateAsync</c> finds nothing pending
/// and returns immediately — one code path, and the chain is still genuinely exercised once per run.
/// </para>
///
/// <para>
/// (2) <b>The connection-pool cap.</b> Every database gets a distinct connection string and Npgsql
/// keeps a separate pool per connection string, so without a cap the pools accumulate until the
/// server answers <c>53300: sorry, too many clients already</c> — which surfaces as a wave of
/// unrelated-looking SetUp failures nowhere near the real cause. Hence the cap here, the raised
/// <c>max_connections</c> on the server, and <see cref="ReleaseDatabase"/>, which factories call on
/// dispose. Note the cap is <b>higher than Identity's 4</b>: Catalog creates far fewer databases
/// (one per fixture, roughly a dozen) but has fixtures that fire ten concurrent HTTP requests at
/// once (<c>ConcurrentOperationTests</c>), and each in-flight request holds a connection.
/// </para>
///
/// <para>
/// Startup is lazy: a run that executes no Postgres-backed test never contacts Docker.
/// </para>
/// </summary>
public static class PostgresTestServer
{
    private const string TemplateDatabase = "catalog_template";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static bool _templateReady;

    /// <summary>
    /// Creates a fresh database cloned from the migrated, seeded template and returns its connection
    /// string. Each caller gets its own database, so fixtures cannot see each other's rows.
    /// </summary>
    public static async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var container = await EnsureTemplateAsync(cancellationToken);

        var databaseName = $"catalog_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(AdminConnectionString(container)))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            // Names are generated GUIDs and a constant, never external input.
            command.CommandText = $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateDatabase}\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return ConnectionStringFor(container, databaseName);
    }

    /// <summary>
    /// Releases the Npgsql pool for a database that is finished with. Factories call this on
    /// dispose; without it each pool holds server connections until the process exits.
    /// </summary>
    public static void ReleaseDatabase(string connectionString)
    {
        // ClearPool only needs the connection string to identify the pool; it does not connect.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
    }

    private static string AdminConnectionString(PostgreSqlContainer container)
        => container.GetConnectionString();

    private static string ConnectionStringFor(PostgreSqlContainer container, string databaseName)
        => new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = databaseName,
            // See the class remarks: one pool per connection string, one connection string per
            // fixture. Do NOT also set ConnectionIdleLifetime below Npgsql's
            // ConnectionPruningInterval (default 10s) — Npgsql rejects that combination while
            // building the data source, so it surfaces from whatever opens the first connection,
            // nowhere near this line.
            MaxPoolSize = 12
        }.ConnectionString;

    private static async Task<PostgreSqlContainer> EnsureTemplateAsync(CancellationToken cancellationToken)
    {
        if (_container is not null && _templateReady)
        {
            return _container;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            _container ??= await StartContainerAsync(cancellationToken);

            if (!_templateReady)
            {
                await BuildTemplateAsync(_container, cancellationToken);
                _templateReady = true;
            }
        }
        finally
        {
            Gate.Release();
        }

        return _container;
    }

    private static async Task<PostgreSqlContainer> StartContainerAsync(CancellationToken cancellationToken)
    {
        var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("catalog_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            // Headroom over the default 100. The pool cap above is the real fix; this is margin so
            // a slow pool release cannot fail an otherwise correct run.
            .WithCommand("-c", "max_connections=300")
            .WithCleanUp(true)
            .Build();

        await container.StartAsync(cancellationToken);
        return container;
    }

    /// <summary>
    /// Applies the migration chain once, to the template, and seeds it. This is also the run's proof
    /// that the migrations apply cleanly to an empty database — including the <c>pg_trgm</c>
    /// extension and the GIN indexes, neither of which any InMemory test could reach.
    /// </summary>
    private static async Task BuildTemplateAsync(
        PostgreSqlContainer container,
        CancellationToken cancellationToken)
    {
        await using (var connection = new NpgsqlConnection(AdminConnectionString(container)))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            // DROP first so a half-built template from a failed earlier attempt cannot poison the
            // run: without this the retry fails with "42P04: database already exists" on every
            // subsequent test, and that misleading error is all anyone sees while the real
            // first-attempt failure scrolls past once.
            command.CommandText =
                $"DROP DATABASE IF EXISTS \"{TemplateDatabase}\"; CREATE DATABASE \"{TemplateDatabase}\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var templateConnectionString = ConnectionStringFor(container, TemplateDatabase);

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(templateConnectionString)
            .Options;

        await using (var context = new CatalogDbContext(options))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Seed the shared categories and products into the template, so every cloned database
        // arrives already seeded. CatalogApiFactory.InitializeDatabaseAsync is idempotent (it checks
        // whether any category exists), so the per-fixture call still runs and simply finds
        // everything already present.
        var seedFactory = PostgresCatalogApiFactory.ForConnectionString(templateConnectionString);
        try
        {
            _ = seedFactory.CreateClient();
            await seedFactory.InitializeDatabaseAsync();
        }
        finally
        {
            seedFactory.Dispose();
        }

        // Postgres refuses CREATE DATABASE ... TEMPLATE while anything is connected to the template,
        // and Npgsql would otherwise keep these connections pooled and idle. The factory dispose
        // above already releases its own pool; this covers the migration context's.
        ReleaseDatabase(templateConnectionString);
    }

    internal static async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            _container = null;
            _templateReady = false;
        }
    }
}
