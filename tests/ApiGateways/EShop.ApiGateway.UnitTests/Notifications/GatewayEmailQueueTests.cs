using EShop.ApiGateway.Notifications;

namespace EShop.ApiGateway.UnitTests.Notifications;

[TestFixture]
public sealed class GatewayEmailQueueTests
{
    [Test]
    public async Task EnqueueAndRead_UpdatesDiagnosticsSnapshot()
    {
        var queue = new GatewayEmailQueue();

        var item = new EmailNotificationContext(
            EventType: "SimulationResponse",
            Route: "orders",
            StatusCode: 200,
            Path: "/test/orders",
            UserId: "u1",
            UserEmail: null,
            CorrelationId: "c1",
            OccurredAtUtc: DateTime.UtcNow);

        await queue.EnqueueAsync(item, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await foreach (var _ in queue.ReadAllAsync(cts.Token))
        {
            break;
        }

        var snapshot = queue.GetSnapshot();

        Assert.That(snapshot.EnqueuedCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(snapshot.DequeuedCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(snapshot.BacklogCount, Is.GreaterThanOrEqualTo(0));
    }
}
