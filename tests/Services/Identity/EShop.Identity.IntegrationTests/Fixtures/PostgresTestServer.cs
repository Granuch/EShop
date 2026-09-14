using EShop.Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EShop.Identity.IntegrationTests.Fixtures;

/// <summary>
/// TEST-01. Owns the single PostgreSQL container shared by every test in this assembly, and hands
/// out a fresh database per caller — which since PERF-02 means one per <i>fixture</i>, not one per
/// test method. See <c>IntegrationTestBase.UseFixtureScopedHost</c>.
///
/// <para>
/// <b>Why the suite is on Postgres at all.</b> Until Stage 8 every integration test ran on EF
/// InMemory, so production paths only a relational provider can execute —
/// <c>ExecuteUpdateAsync</c>, <c>ExecuteDeleteAsync</c>, column limits, unique indexes, concurrency
/// tokens — were unreachable from any test. <c>RefreshTokenRepository</c> and
/// <c>TokenCleanupService</c> had grown explicit <c>IsInMemory()</c> forks as a result: production
/// ran one query, every test ran a different one. Removing those forks is what made a real
/// database mandatory rather than optional.
/// </para>
///
/// <para>
/// <b>Two things here are not incidental, and removing either breaks the suite.</b>
/// </para>
///
/// <para>
/// (1) <b>The template database.</b> Applying the full migration chain per test cost ~1.7 s and
/// took the suite from 1m20s to <b>six minutes</b>. Migrations are applied once to a template, and
/// each test database is produced with <c>CREATE DATABASE … TEMPLATE</c>, which Postgres does as a
/// file copy. The copy carries <c>__EFMigrationsHistory</c> with it, so the app's own startup
/// <c>MigrateAsync</c> finds nothing pending and returns immediately — no second code path, and
/// the migration chain is still genuinely exercised once per run.
/// </para>
///
/// <para>
/// (2) <b>The connection-pool cap.</b> Every database gets a distinct connection string, and Npgsql
/// keeps a separate pool per connection string. With the default pool size, 154 tests exhausted
/// the server: <c>53300: sorry, too many clients already</c>, which surfaces as a pile of
/// unrelated-looking SetUp failures. Hence the small <c>Maximum Pool Size</c> here, the raised
/// <c>max_connections</c> on the server, and <see cref="ReleaseDatabase"/>, which factories call on
/// dispose so a finished fixture's pool does not sit on connections for the rest of the run.
/// PERF-02 reduced the pressure a great deal — the full suite now creates about 30 databases
/// rather than one per test — but it did not remove it, so keep all three.
/// </para>
///
/// <para>
/// Startup is lazy: an assembly that runs no integration tests never contacts Docker.
/// </para>
/// </summary>
public static class PostgresTestServer
{
    private const string TemplateDatabase = "identity_template";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static bool _templateReady;

    /// <summary>
    /// Creates a fresh database cloned from the migrated template and returns its connection
    /// string. Each caller gets its own database, so tests cannot see each other's rows.
    /// </summary>
    public static async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var container = await EnsureTemplateAsync(cancellationToken);

        var databaseName = $"identity_{Guid.NewGuid():N}";

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
    /// dispose; without it each test's pool holds server connections until the process exits and
    /// the run dies partway through with <c>53300: sorry, too many clients already</c>.
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
            // test. Keep each pool tiny or the server runs out of backends mid-run. Do NOT also
            // set ConnectionIdleLifetime below Npgsql's ConnectionPruningInterval (default 10s) —
            // Npgsql rejects that combination at construction with an ArgumentException, and
            // because it throws while building the data source it surfaces from whatever happens
            // to open the first connection, nowhere near this line.
            MaxPoolSize = 4
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
            .WithDatabase("identity_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            // Headroom over the default 100. The pool cap above is the real fix; this is margin
            // so a slow pool release cannot fail an otherwise correct run.
            .WithCommand("-c", "max_connections=300")
            .WithCleanUp(true)
            .Build();

        await container.StartAsync(cancellationToken);
        return container;
    }

    /// <summary>
    /// Applies the migration chain once, to the template. This is also the run's proof that the
    /// migrations apply cleanly to an empty database.
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
            // run: without this, the retry fails with "42P04: database already exists" on every
            // subsequent test and that misleading error is all anyone sees, while the real
            // first-attempt failure scrolls past once.
            command.CommandText =
                $"DROP DATABASE IF EXISTS \"{TemplateDatabase}\"; CREATE DATABASE \"{TemplateDatabase}\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var templateConnectionString = ConnectionStringFor(container, TemplateDatabase);

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(templateConnectionString)
            .Options;

        await using (var context = new IdentityDbContext(options))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Seed the shared test users into the template as well, so every cloned database arrives
        // already seeded. Seeding creates four users with real ASP.NET Identity password hashes,
        // and PBKDF2 is deliberately expensive — doing it once per run instead of once per test
        // is worth more than the database clone itself. IdentityApiFactory.InitializeDatabaseAsync
        // is idempotent (it checks FindByEmailAsync first), so the per-test call still runs and
        // simply finds everything already present.
        var seedFactory = PostgresIdentityApiFactory.ForConnectionString(templateConnectionString);
        try
        {
            _ = seedFactory.CreateClient();
            await seedFactory.InitializeDatabaseAsync();
        }
        finally
        {
            seedFactory.Dispose();
        }

        // Postgres refuses CREATE DATABASE ... TEMPLATE while anything is connected to the
        // template, and Npgsql would otherwise keep these connections pooled and idle. The factory
        // dispose above already releases its own pool; this covers the migration context's.
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
