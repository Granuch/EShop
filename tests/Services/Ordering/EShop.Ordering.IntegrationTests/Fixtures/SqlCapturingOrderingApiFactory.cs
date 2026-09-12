using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EShop.Ordering.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres host, recording the text of every query EF sends. For properties of a query that the
/// rows cannot reveal — above all its <c>ORDER BY</c>, which an index can make look correct when it is not.
/// </summary>
public sealed class SqlCapturingOrderingApiFactory : PostgresOrderingApiFactory
{
    private readonly CommandCapture _capture = new();

    private SqlCapturingOrderingApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static new async Task<SqlCapturingOrderingApiFactory> CreateAsync(CancellationToken cancellationToken = default)
        => new(await PostgresTestServer.CreateDatabaseAsync(cancellationToken));

    /// <summary>Every reader command sent since the last <c>Clear()</c>, in order.</summary>
    public ConcurrentQueue<string> Commands => _capture.Commands;

    /// <summary>One interceptor instance for the life of the host, so EF's options stay equal between scopes.</summary>
    protected override void ConfigureNpgsql(DbContextOptionsBuilder options) => options.AddInterceptors(_capture);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
