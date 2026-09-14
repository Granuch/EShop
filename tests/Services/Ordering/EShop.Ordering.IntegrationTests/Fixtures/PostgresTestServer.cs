using EShop.Ordering.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EShop.Ordering.IntegrationTests.Fixtures;

/// <summary>
/// Ordering audit M11, ported from Catalog's Stage 0. Owns the single PostgreSQL container shared by
/// every test in this assembly and hands out a fresh database per caller — one per <i>fixture</i>,
/// since the host is fixture-scoped (see <c>IntegrationTestBase.UseFixtureScopedHost</c>).
///
/// <para>
/// <b>Why Ordering is on Postgres.</b> On EF InMemory the relational half of this service could not be
/// observed: the <c>(CreatedAt, Id)</c> list order (InMemory sorts stably, so a broken tie-break stayed
/// green), the <c>xmin</c> concurrency token, <c>IdempotentConsumer</c>'s <c>ON CONFLICT</c> claim and
/// the outbox's <c>FOR UPDATE SKIP LOCKED</c>, the <c>numeric(18,2)</c> and varchar limits, and the
/// migration chain itself.
/// </para>
///
/// <para>
/// <b>Two things here are load-bearing</b>, exactly as in Catalog. (1) The template database: migrations
/// run once into it and each database is a <c>CREATE DATABASE … TEMPLATE</c> file copy that carries
/// <c>__EFMigrationsHistory</c>, so the host's own startup <c>MigrateAsync</c> finds nothing pending.
/// (2) The pool cap and <see cref="ReleaseDatabase"/>: Npgsql pools per connection string and every
/// database has its own, so without them the server eventually answers
/// <c>53300: sorry, too many clients already</c>, surfacing as unrelated-looking SetUp failures.
/// </para>
///
/// <para>Startup is lazy: a run with no Postgres-backed test never contacts Docker.</para>
/// </summary>
public static class PostgresTestServer
{
    private const string TemplateDatabase = "ordering_template";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static bool _templateReady;

    /// <summary>Creates a fresh database cloned from the migrated template and returns its connection string.</summary>
    public static async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var container = await EnsureTemplateAsync(cancellationToken);

        var databaseName = $"ordering_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            // Names are generated GUIDs and a constant, never external input.
            command.CommandText = $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateDatabase}\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return ConnectionStringFor(container, databaseName);
    }

    /// <summary>Releases the Npgsql pool of a finished database. Factories call this on dispose.</summary>
    public static void ReleaseDatabase(string connectionString)
    {
        // ClearPool only needs the connection string to identify the pool; it does not connect.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
    }

    private static string ConnectionStringFor(PostgreSqlContainer container, string databaseName)
        => new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = databaseName,
            // One pool per fixture. Do NOT also set ConnectionIdleLifetime below Npgsql's
            // ConnectionPruningInterval (10 s): Npgsql rejects the combination while building the data
            // source, so it surfaces from whatever opens the first connection, nowhere near here.
            MaxPoolSize = 8
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
            .WithDatabase("ordering_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            // Headroom over the default 100; the pool cap is the real fix.
            .WithCommand("-c", "max_connections=300")
            .WithCleanUp(true)
            .Build();

        await container.StartAsync(cancellationToken);
        return container;
    }

    /// <summary>
    /// Applies the migration chain once, to the template — which is also the run's proof that the
    /// migrations apply cleanly to an empty database. Not seeded: the factory's seed is a single order
    /// and idempotent, so each database seeds itself.
    /// </summary>
    private static async Task BuildTemplateAsync(PostgreSqlContainer container, CancellationToken cancellationToken)
    {
        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            // DROP first, so a half-built template from a failed attempt cannot turn every later test
            // into a misleading "42P04: database already exists".
            command.CommandText =
                $"DROP DATABASE IF EXISTS \"{TemplateDatabase}\"; CREATE DATABASE \"{TemplateDatabase}\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var templateConnectionString = ConnectionStringFor(container, TemplateDatabase);

        await using (var context = new OrderingDbContext(
            new DbContextOptionsBuilder<OrderingDbContext>().UseNpgsql(templateConnectionString).Options))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Postgres refuses CREATE DATABASE ... TEMPLATE while anything is connected to the template.
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
