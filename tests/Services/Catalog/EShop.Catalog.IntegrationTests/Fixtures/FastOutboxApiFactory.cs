using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres host with <c>OutboxProcessorService</c> polling fast enough to assert on.
///
/// <para>
/// The processor already runs in every test host, but with production pacing: after each empty poll
/// it backs off exponentially up to <b>30 seconds</b>, and an integration event takes <i>two</i>
/// polls to appear (domain-event row → handler → integration-event row). A fixture that has been idle
/// would wait out the full back-off before seeing anything. Capping it keeps the two-hop chain under a
/// second. There is no RabbitMQ under Testing (<c>RabbitMQ:Host</c> is blank, so no bus is
/// registered), which is fine: the integration-event row is written by the first hop's
/// <c>SaveChanges</c>, and that row is what these tests read.
/// </para>
/// </summary>
public class FastOutboxApiFactory : PostgresCatalogApiFactory
{
    private FastOutboxApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static async Task<FastOutboxApiFactory> CreateAsync(CancellationToken cancellationToken = default)
        => new(await PostgresTestServer.CreateDatabaseAsync(cancellationToken));

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        services.RemoveAll<OutboxProcessorOptions>();
        services.AddSingleton(new OutboxProcessorOptions
        {
            BatchSize = 20,
            PollingIntervalMs = 50,
            MaxPollingIntervalMs = 250,
            MaxRetries = 5,
            ErrorRetryDelayMs = 250
        });
    }
}
