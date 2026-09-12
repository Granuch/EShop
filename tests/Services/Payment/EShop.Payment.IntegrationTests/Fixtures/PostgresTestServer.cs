using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EShop.Payment.IntegrationTests.Fixtures;

/// <summary>
/// Ordering audit Stage 20, ported from Ordering's fixture of the same name. Owns one PostgreSQL container
/// for the assembly and hands out a fresh database per caller, cloned from a template that the migration
/// chain was applied to once.
///
/// <para>
/// <b>Why Payment needs a real database at all.</b> The rest of this suite runs on EF InMemory, which has
/// no <c>xmin</c>, no transactions and no <c>ON CONFLICT</c>. Two writers meeting on one payment row is
/// decided by exactly those: the payment's <c>xmin</c> concurrency token, and <c>IdempotentConsumer</c>'s
/// transaction rolling back its message claim. See <c>Persistence/PaymentWriteCollisionTests</c>.
/// </para>
///
/// <para>
/// Two things are load-bearing, as in Ordering and Catalog. (1) The template: each database is a
/// <c>CREATE DATABASE … TEMPLATE</c> file copy that already carries <c>__EFMigrationsHistory</c>. (2) The pool
/// cap and <see cref="ReleaseDatabase"/>: Npgsql pools per connection string, so without them the server
/// eventually answers <c>53300: sorry, too many clients already</c>.
/// </para>
///
/// <para>Startup is lazy: a run with no Postgres-backed test never contacts Docker.</para>
/// </summary>
public static class PostgresTestServer
{
    private const string TemplateDatabase = "payment_template";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static bool _templateReady;

    /// <summary>Creates a fresh database cloned from the migrated template and returns its connection string.</summary>
    public static async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var container = await EnsureTemplateAsync(cancellationToken);

        var databaseName = $"payment_{Guid.NewGuid():N}";

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

    /// <summary>Releases the Npgsql pool of a finished database.</summary>
    public static void ReleaseDatabase(string connectionString)
    {
        // ClearPool only needs the connection string to identify the pool; it does not connect.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
    }

    private static string ConnectionStringFor(PostgreSqlContainer container, string databaseName)
        => new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = databaseName,
            // Do NOT also set ConnectionIdleLifetime below Npgsql's ConnectionPruningInterval (10 s): Npgsql
            // rejects the combination while building the data source, far from here.
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
            .WithDatabase("payment_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .Build();

        await container.StartAsync(cancellationToken);
        return container;
    }

    /// <summary>
    /// Applies the migration chain once, to the template, which also proves the chain applies to an empty
    /// database.
    /// </summary>
    private static async Task BuildTemplateAsync(PostgreSqlContainer container, CancellationToken cancellationToken)
    {
        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            // DROP first, so a half-built template from a failed attempt cannot turn every later test into a
            // misleading "42P04: database already exists".
            command.CommandText =
                $"DROP DATABASE IF EXISTS \"{TemplateDatabase}\"; CREATE DATABASE \"{TemplateDatabase}\";";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var templateConnectionString = ConnectionStringFor(container, TemplateDatabase);

        await using (var context = new PaymentDbContext(
            new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(templateConnectionString).Options))
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
