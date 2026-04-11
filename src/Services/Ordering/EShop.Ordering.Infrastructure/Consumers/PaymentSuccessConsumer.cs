using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using EShop.BuildingBlocks.Application.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EShop.Ordering.Infrastructure.Consumers;

/// <summary>
/// Idempotent consumer for PaymentSuccessEvent.
/// Marks the order as paid and immediately ships it.
/// </summary>
public class PaymentSuccessConsumer : IdempotentConsumer<PaymentSuccessEvent, OrderingDbContext>
{
    private readonly IDistributedCache _cache;
    private readonly CachingBehaviorOptions _cachingOptions;
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConnectionMultiplexer? _redis;

    public PaymentSuccessConsumer(
        OrderingDbContext dbContext,
        IDistributedCache cache,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        Microsoft.Extensions.Options.IOptions<CachingBehaviorOptions> cachingOptions,
        ILogger<PaymentSuccessConsumer> logger,
        IConnectionMultiplexer? redis = null)
        : base(dbContext, logger)
    {
        _cache = cache;
        _cachingOptions = cachingOptions.Value;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _redis = redis;
    }

    protected override async Task HandleAsync(ConsumeContext<PaymentSuccessEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;

        Logger.LogInformation(
            "Processing PaymentSuccessEvent for OrderId={OrderId}, PaymentIntentId={PaymentIntentId}",
            message.OrderId,
            message.PaymentIntentId);

        var order = await _orderRepository.GetByIdAsync(message.OrderId, cancellationToken);
        if (order is null)
        {
            Logger.LogWarning("Order {OrderId} not found for PaymentSuccessEvent", message.OrderId);
            return;
        }

        if (order.Status == OrderStatus.Paid)
        {
            Logger.LogInformation(
                "Order {OrderId} already marked as paid. Skipping duplicate PaymentSuccessEvent.",
                message.OrderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            Logger.LogWarning(
                "Skipping PaymentSuccessEvent for OrderId={OrderId} because order status is {Status}.",
                message.OrderId,
                order.Status);
            return;
        }

        order.MarkAsPaid(message.PaymentIntentId);
        order.Ship();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await InvalidateUserOrdersCacheAsync(order.UserId, cancellationToken);

        Logger.LogInformation("Order {OrderId} marked as paid and shipped by payment-success orchestration", message.OrderId);
    }

    private async Task InvalidateUserOrdersCacheAsync(string userId, CancellationToken cancellationToken)
    {
        if (_redis != null)
        {
            try
            {
                var database = _redis.GetDatabase();
                var dbNumber = database.Database;
                var keyPattern = $"*orders:user:{userId}:*";
                var deletedCount = 0L;

                foreach (var endpoint in _redis.GetEndPoints(configuredOnly: true))
                {
                    var server = _redis.GetServer(endpoint);
                    if (!server.IsConnected || server.IsReplica)
                    {
                        continue;
                    }

                    foreach (var key in server.Keys(dbNumber, keyPattern, pageSize: 250))
                    {
                        if (await database.KeyDeleteAsync(key))
                        {
                            deletedCount++;
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    Logger.LogDebug("Invalidated {Count} user order cache entries for UserId={UserId}", deletedCount, userId);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "Prefix cache invalidation failed for UserId={UserId}. Falling back to known keys.",
                    userId);
            }
        }

        var baseUserOrdersKey = $"orders:user:{userId}:";
        int[] knownPageSizes = [5, 10, 20, 25, 50];
        foreach (var ps in knownPageSizes)
        {
            await InvalidateCacheAsync($"{baseUserOrdersKey}p=1:ps={ps}:cur=", cancellationToken);
        }
    }

    private Task InvalidateCacheAsync(string keyPattern, CancellationToken cancellationToken)
    {
        var fullKey = _cachingOptions.UseVersioning
            ? $"{_cachingOptions.KeyPrefix}{_cachingOptions.Version}:{keyPattern}"
            : $"{_cachingOptions.KeyPrefix}{keyPattern}";

        return _cache.RemoveAsync(fullKey, cancellationToken);
    }
}
