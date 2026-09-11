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
/// Marks the order as paid, and also ships it only when
/// <see cref="PaymentSuccessProcessingOptions.AutoShipOnPaymentSuccess"/> is on (off by default).
/// </summary>
public class PaymentSuccessConsumer : IdempotentConsumer<PaymentSuccessEvent, OrderingDbContext>
{
    private readonly IDistributedCache _cache;
    private readonly CachingBehaviorOptions _cachingOptions;
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConnectionMultiplexer? _redis;
    private readonly PaymentSuccessProcessingOptions _processingOptions;

    public PaymentSuccessConsumer(
        OrderingDbContext dbContext,
        IDistributedCache cache,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        Microsoft.Extensions.Options.IOptions<CachingBehaviorOptions> cachingOptions,
        Microsoft.Extensions.Options.IOptions<PaymentSuccessProcessingOptions>? processingOptions,
        ILogger<PaymentSuccessConsumer> logger,
        IConnectionMultiplexer? redis = null)
        : base(dbContext, logger)
    {
        _cache = cache;
        _cachingOptions = cachingOptions.Value;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _redis = redis;
        _processingOptions = processingOptions?.Value ?? new PaymentSuccessProcessingOptions();
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

        if (order.Status == OrderStatus.Cancelled)
        {
            // The customer was charged for an order that was cancelled while the payment was in flight.
            // Nothing refunds that automatically — there is no refund flow — so it needs a person, and a
            // retry would change nothing. Logged at Error so it is not lost among routine skips.
            Logger.LogError(
                "PaymentSuccessEvent for cancelled OrderId={OrderId}: PaymentIntentId={PaymentIntentId}, "
                + "Amount={Amount} was captured but the order is cancelled. Manual refund required.",
                message.OrderId,
                message.PaymentIntentId,
                message.Amount);
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

        // Throws DomainException when message.Amount differs from the order total. That is deliberate:
        // the message retries and then lands in the error queue with its payload intact for
        // reconciliation, rather than being acknowledged and forgotten.
        order.MarkAsPaid(message.PaymentIntentId, message.Amount);

        if (_processingOptions.AutoShipOnPaymentSuccess)
        {
            order.Ship();
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await InvalidateUserOrdersCacheAsync(order.UserId, cancellationToken);

        Logger.LogInformation(
            _processingOptions.AutoShipOnPaymentSuccess
                ? "Order {OrderId} marked as paid and shipped by payment-success orchestration"
                : "Order {OrderId} marked as paid by payment-success orchestration",
            message.OrderId);
    }

    public sealed class PaymentSuccessProcessingOptions
    {
        public const string SectionName = "PaymentSuccessProcessing";

        /// <summary>
        /// Off by default: a paid order stays Paid until an admin ships it. When on, payment success
        /// ships the order in the same transaction, so the Paid state and POST /orders/{id}/ship are
        /// effectively skipped.
        /// </summary>
        public bool AutoShipOnPaymentSuccess { get; init; }
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
