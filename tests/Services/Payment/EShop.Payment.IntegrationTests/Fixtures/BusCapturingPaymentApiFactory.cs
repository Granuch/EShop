using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Payment.IntegrationTests.Fixtures;

/// <summary>
/// The Payment host with an in-memory bus (MassTransit's test harness) and the outbox processor polling
/// fast enough to assert on.
///
/// <para>
/// Under Testing <c>RabbitMQ:Host</c> is blank, so <c>AddMessaging</c> registers no bus at all and the
/// outbox processor has nothing to publish to. The harness supplies one and records every publish, which
/// is what lets a test see an event actually leave Payment rather than merely reach
/// <c>outbox_messages</c> — the step that was missing until Ordering audit Stage 10. The processor's
/// production back-off grows to 30 seconds on an idle host; the options below cap it.
/// </para>
/// </summary>
public sealed class BusCapturingPaymentApiFactory : PaymentApiFactory
{
    internal static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness(bus =>
                bus.SetTestTimeouts(testTimeout: PublishTimeout, testInactivityTimeout: PublishTimeout));

            services.RemoveAll<OutboxProcessorOptions>();
            services.AddSingleton(new OutboxProcessorOptions
            {
                BatchSize = 20,
                PollingIntervalMs = 50,
                MaxPollingIntervalMs = 250,
                MaxRetries = 5,
                ErrorRetryDelayMs = 250
            });
        });
    }
}
