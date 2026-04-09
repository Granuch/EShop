using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Health;
using EShop.ApiGateway.Notifications;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Health;

[TestFixture]
public sealed class EmailQueueHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenQueueIsEmpty()
    {
        var queue = new GatewayEmailQueue();
        var options = Options.Create(new EmailQueueHealthOptions
        {
            BacklogWarningThreshold = 10,
            BacklogUnhealthyThreshold = 50,
            DroppedWarningThreshold = 1,
            DroppedUnhealthyThreshold = 5
        });

        var check = new EmailQueueHealthCheck(queue, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task CheckHealthAsync_ReturnsDegraded_WhenDroppedCountExceedsWarningThreshold()
    {
        var queue = new GatewayEmailQueue();

        var options = Options.Create(new EmailQueueHealthOptions
        {
            BacklogWarningThreshold = 5000,
            BacklogUnhealthyThreshold = 10000,
            DroppedWarningThreshold = 1,
            DroppedUnhealthyThreshold = 1000
        });

        for (var i = 0; i < 2500; i++)
        {
            await queue.EnqueueAsync(CreateItem(i), CancellationToken.None);
        }

        var check = new EmailQueueHealthCheck(queue, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }

    [Test]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenDroppedCountExceedsUnhealthyThreshold()
    {
        var queue = new GatewayEmailQueue();

        var options = Options.Create(new EmailQueueHealthOptions
        {
            BacklogWarningThreshold = 5000,
            BacklogUnhealthyThreshold = 10000,
            DroppedWarningThreshold = 1,
            DroppedUnhealthyThreshold = 2
        });

        for (var i = 0; i < 2503; i++)
        {
            await queue.EnqueueAsync(CreateItem(i), CancellationToken.None);
        }

        var check = new EmailQueueHealthCheck(queue, options);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    private static EmailNotificationContext CreateItem(int index)
    {
        return new EmailNotificationContext(
            EventType: "SimulationResponse",
            Route: "orders",
            StatusCode: 200,
            Path: "/orders",
            UserId: $"u-{index}",
            UserEmail: null,
            CorrelationId: $"c-{index}",
            OccurredAtUtc: DateTime.UtcNow);
    }
}
