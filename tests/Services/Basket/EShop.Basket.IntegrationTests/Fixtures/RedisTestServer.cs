using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Fixtures;

/// <summary>
/// Basket audit S2 (tech debt 8). Owns the single Redis container shared by every test in this assembly and
/// hands each caller its own logical database, so one factory's baskets, reverse index and outbox never leak
/// into another's.
///
/// <para>
/// <b>Why Basket's suite is on real Redis.</b> Until S2, <c>BasketApiFactory</c> swapped
/// <c>IConnectionMultiplexer</c> for a Moq stub whose <c>IDatabase</c> returned defaults, so every basket read as
/// absent and nothing below the HTTP layer ran: not the repository, not its <c>MULTI</c> transaction or reverse
/// index, not TTLs, and not the <c>WATCH</c> conditions the atomicity and concurrency stages depend on. A mocked
/// <c>IDatabase</c> accepts any condition, so those fixes could not have been tested against it.
/// </para>
///
/// <para>
/// <b>Isolation is by database index, not by container.</b> The server starts with
/// <see cref="DatabaseCount"/> databases and each caller gets the next index, flushed first; the index reaches
/// the host through <c>defaultDatabase=N</c> in the connection string, which the multiplexer's
/// <c>GetDatabase()</c> and the Redis cache both honour. Startup is lazy: a run with no Redis-backed test never
/// contacts Docker.
/// </para>
/// </summary>
public static class RedisTestServer
{
    private const int RedisPort = 6379;
    private const int DatabaseCount = 4096;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IContainer? _container;
    private static ConnectionMultiplexer? _admin;
    private static int _lastDatabase = -1;

    /// <summary>Allocates an empty logical database and returns a connection string that selects it.</summary>
    public static async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var (container, admin) = await EnsureStartedAsync(cancellationToken);

        var database = Interlocked.Increment(ref _lastDatabase) % DatabaseCount;

        // Flushed rather than assumed empty, so a run with more than DatabaseCount factories reuses an index safely.
        await admin.GetServer(admin.GetEndPoints()[0]).FlushDatabaseAsync(database);

        return $"{Endpoint(container)},abortConnect=false,defaultDatabase={database}";
    }

    private static string Endpoint(IContainer container)
        => $"{container.Hostname}:{container.GetMappedPublicPort(RedisPort)}";

    private static async Task<(IContainer Container, ConnectionMultiplexer Admin)> EnsureStartedAsync(
        CancellationToken cancellationToken)
    {
        if (_container is not null && _admin is not null)
        {
            return (_container, _admin);
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_container is null)
            {
                var container = new ContainerBuilder()
                    .WithImage("redis:7-alpine")
                    .WithPortBinding(RedisPort, assignRandomHostPort: true)
                    // No persistence: the data is throwaway, and a background save only costs time.
                    .WithCommand("redis-server", "--databases", DatabaseCount.ToString(), "--save", "", "--appendonly", "no")
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
                    .WithCleanUp(true)
                    .Build();

                await container.StartAsync(cancellationToken);
                _container = container;
            }

            _admin ??= await ConnectionMultiplexer.ConnectAsync($"{Endpoint(_container)},allowAdmin=true");
        }
        finally
        {
            Gate.Release();
        }

        return (_container, _admin);
    }

    internal static async Task DisposeAsync()
    {
        if (_admin is not null)
        {
            await _admin.DisposeAsync();
            _admin = null;
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }
}
