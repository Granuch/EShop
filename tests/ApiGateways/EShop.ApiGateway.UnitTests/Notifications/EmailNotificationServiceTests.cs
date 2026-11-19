using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Notifications;

[TestFixture]
public sealed class EmailNotificationServiceTests
{
    private static EmailNotificationContext Notice()
        => new("RateLimitExceeded", "catalog-products-read-route", 429, "/api/v1/products", null, null, "corr", DateTime.UtcNow);

    [Test]
    public async Task AHeldBackNotice_NeverReachesTheQueue_AndTheNextOneSaysHowManyWere()
    {
        var queue = new GatewayEmailQueue();
        var clock = new SteppingTimeProvider(new DateTimeOffset(2026, 11, 18, 22, 39, 0, TimeSpan.Zero));
        var throttle = new GatewayNoticeThrottle(
            clock,
            Options.Create(new GatewayOptions { EmailNotificationWindowSeconds = 900, MaxEmailNotificationsPerDay = 50 }),
            NullLogger<GatewayNoticeThrottle>.Instance);
        var service = new EmailNotificationService(queue, throttle);

        await service.QueueAsync(Notice());
        for (var i = 0; i < 4; i++)
        {
            await service.QueueAsync(Notice());
        }

        clock.Now += TimeSpan.FromMinutes(15);
        await service.QueueAsync(Notice());

        Assert.That(queue.GetSnapshot().EnqueuedCount, Is.EqualTo(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var queued = new List<EmailNotificationContext>();
        await foreach (var item in queue.ReadAllAsync(cts.Token))
        {
            queued.Add(item);
            if (queued.Count == 2)
            {
                break;
            }
        }

        Assert.That(queued.Select(n => n.SuppressedCount), Is.EqualTo(new[] { 0, 4 }));
    }

    private sealed class SteppingTimeProvider(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
