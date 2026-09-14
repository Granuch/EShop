using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Infrastructure.Outbox;

/// <summary>
/// Basket audit L11. Where RabbitMQ is not configured (Development and Testing may run without it) neither MassTransit
/// nor the outbox processor is registered, yet checkout still succeeds and queues its event. Say so once at startup, so a
/// developer wondering why no order appeared is told where the checkouts are.
/// </summary>
internal sealed class OutboxNotDrainedWarning(ILogger<OutboxNotDrainedWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "RabbitMQ is not configured, so Basket's outbox is not drained: checkouts are queued in {PendingKey} and no order is created until messaging is configured",
            BasketOutboxKeys.Pending);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
