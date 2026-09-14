using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.Notification.Infrastructure.BackgroundServices;
using EShop.Notification.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EShop.Notification.IntegrationTests.HealthChecks;

/// <summary>
/// Notification audit S6 (M11, L11, debt 4; D9), read from the host's own registrations, as Payment's
/// <c>OutboxDispatchTests</c> does.
/// </summary>
[TestFixture]
public class HealthCheckRegistrationTests
{
    [Test]
    public async Task Readiness_ChecksTheDatabase_NotSmtpOrAnOutbox()
    {
        await using var factory = new NotificationApiFactory();

        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToDictionary(r => r.Name);

        Assert.Multiple(() =>
        {
            Assert.That(registrations["notification-db"].Tags, Does.Contain("ready"));
            Assert.That(registrations["notification-liveness"].Tags, Does.Contain("live"));
            Assert.That(registrations["smtp"].Tags, Does.Not.Contain("ready"),
                "M11: an SMTP check on readiness logged in on every probe and gated no traffic");
            Assert.That(registrations.ContainsKey("outbox"), Is.False,
                "L11: Notification publishes nothing, so an outbox check only ever reported an empty table");
        });
    }

    [Test]
    public async Task NoOutboxWorkerRuns_ButTheLogRetentionDoes()
    {
        await using var factory = new NotificationApiFactory();

        var hosted = factory.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(hosted, Does.Not.Contain(typeof(OutboxProcessorService)), "debt 4: it polled an empty table");
            Assert.That(hosted, Does.Not.Contain(typeof(OutboxCleanupService)));
            Assert.That(hosted, Does.Contain(typeof(NotificationLogRetentionService)), "control: D8's job still runs");
        });
    }
}
