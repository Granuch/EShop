using EShop.Payment.Infrastructure.Extensions;
using EShop.Payment.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EShop.Payment.UnitTests.Services;

/// <summary>
/// Payment audit Stage 10 (D13). Processed Stripe webhook events are kept for 30 days, and the job that deletes them
/// runs in every host. What the delete removes is checked on Postgres in
/// <c>Persistence/ProcessedStripeWebhookEventRetentionTests</c>.
/// </summary>
[TestFixture]
public class WebhookEventRetentionTests
{
    [Test]
    public void TheCleanup_IsRegisteredAsAHostedService()
    {
        var services = new ServiceCollection();
        services.AddPaymentInfrastructure(new ConfigurationBuilder().Build(), useInMemoryDatabase: true);

        Assert.That(
            services.Any(d => d.ServiceType == typeof(IHostedService)
                              && d.ImplementationType == typeof(ProcessedStripeWebhookEventCleanupService)),
            Is.True);
    }

    [Test]
    public void EventsAreKept_ForThirtyDays()
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

        Assert.That(ProcessedStripeWebhookEventCleanupService.CutoffFor(now), Is.EqualTo(now.AddDays(-30)));
    }
}
