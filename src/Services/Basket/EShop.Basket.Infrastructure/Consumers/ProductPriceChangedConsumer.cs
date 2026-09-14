using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Idempotency;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Messaging.Events;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Infrastructure.Consumers;

/// <summary>
/// Re-prices the baskets that hold a product whose price changed.
///
/// <para><b>Each basket is written like any other basket write (Basket audit S4).</b> The save is conditioned on what
/// was read and retried on a fresh read if the customer changed the basket meanwhile, so an item they add during the
/// sync is kept, and their own later edit cannot put the old price back. A user the reverse index lists but whose
/// basket no longer holds the product — it expired, or the product was removed — is dropped from the index (M8), and a
/// basket already at the new price is not rewritten.</para>
/// </summary>
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
                var outcome = await BasketWrites.RunAsync(
                    ct => RepriceAsync(userId, message, ct),
                    context.CancellationToken);

                if (outcome.IsFailure)
                {
                    // Not marked processed, so the message is redelivered and the whole fan-out re-run; re-pricing a
                    // basket that already has the new price writes nothing.
                    throw new InvalidOperationException(
                        $"The basket of user '{userId}' kept changing while ProductId={message.ProductId} was re-priced.");
                }
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

    /// <summary>One attempt for one basket; <c>null</c> when its conditional write lost a race.</summary>
    private async Task<Result<Unit>?> RepriceAsync(
        string userId,
        ProductPriceChangedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        var basket = await _basketRepository.GetBasketAsync(userId, cancellationToken);

        if (basket == null || basket.Items.All(item => item.ProductId != message.ProductId))
        {
            if (!await _basketRepository.TryRemoveFromProductIndexAsync(message.ProductId, userId, basket, cancellationToken))
            {
                return null;
            }

            return BasketWrites.Done;
        }

        if (!basket.ApplyPriceChange(message.ProductId, message.NewPrice))
        {
            return BasketWrites.Done;
        }

        if (!await _basketRepository.TrySaveBasketAsync(basket, cancellationToken))
        {
            return null;
        }

        return BasketWrites.Done;
    }
}
