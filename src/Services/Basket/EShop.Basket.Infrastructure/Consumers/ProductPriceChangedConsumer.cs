using EShop.Basket.Application.Abstractions;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Idempotency;
using EShop.BuildingBlocks.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Infrastructure.Consumers;

public class ProductPriceChangedConsumer : IConsumer<ProductPriceChangedIntegrationEvent>
{
    private readonly IBasketRepository _basketRepository;
    private readonly RedisMessageIdempotencyStore _idempotencyStore;
    private readonly ILogger<ProductPriceChangedConsumer> _logger;
    private readonly IBasketMetrics _metrics;

    public ProductPriceChangedConsumer(
        IBasketRepository basketRepository,
        RedisMessageIdempotencyStore idempotencyStore,
        ILogger<ProductPriceChangedConsumer> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<ProductPriceChangedIntegrationEvent> context)
    {
        var message = context.Message;
        var messageId = context.MessageId ?? message.EventId;

        if (await _idempotencyStore.IsProcessedAsync(messageId))
        {
            _logger.LogInformation(
                "Skipping duplicate ProductPriceChangedIntegrationEvent. MessageId={MessageId}",
                messageId);
            return;
        }

        var lockAcquired = await _idempotencyStore.TryBeginProcessingAsync(messageId, TimeSpan.FromMinutes(5));
        if (!lockAcquired)
        {
            _logger.LogInformation(
                "Skipping concurrently processed ProductPriceChangedIntegrationEvent. MessageId={MessageId}",
                messageId);
            return;
        }

        try
        {
            var userIds = await _basketRepository.GetUsersContainingProductAsync(message.ProductId, context.CancellationToken);
            if (userIds.Count == 0)
            {
                await _idempotencyStore.TryMarkProcessedAsync(messageId, TimeSpan.FromDays(7));
                return;
            }

            foreach (var userId in userIds)
            {
                var basket = await _basketRepository.GetBasketAsync(userId, context.CancellationToken);
                if (basket == null)
                {
                    continue;
                }

                basket.ApplyPriceChange(message.ProductId, message.NewPrice);
                await _basketRepository.SaveBasketAsync(basket, context.CancellationToken);
            }

            await _idempotencyStore.TryMarkProcessedAsync(messageId, TimeSpan.FromDays(7));

            _metrics.RecordPriceSyncUpdate("success");
            _logger.LogInformation(
                "Synchronized basket prices for ProductId={ProductId}, UserCount={UserCount}",
                message.ProductId,
                userIds.Count);
        }
        catch (Exception ex)
        {
            _metrics.RecordPriceSyncUpdate("failure");
            _logger.LogError(ex,
                "Failed to synchronize basket prices for ProductId={ProductId}",
                message.ProductId);
            throw;
        }
        finally
        {
            await _idempotencyStore.CompleteProcessingAsync(messageId);
        }
    }
}
