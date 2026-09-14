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
///
/// <para><b>Only the newest change is applied (S9, M10).</b> <see cref="PriceChangeWatermark"/> records the newest
/// change per product; an event older than it changes nothing, whether it arrives late or runs alongside the newer one.
/// The second check sits after each basket read, which is what makes the race safe: a newer event records itself before
/// it writes any basket, so a read that sees its write also sees its record, and a write it makes after this read makes
/// this save fail and the attempt re-read.</para>
///
/// <para><b>The fan-out runs <see cref="MaxConcurrentBaskets"/> baskets at a time (S9, M9).</b> It was a sequential
/// loop, so a popular product cost several round trips per user, one after another, inside one message.</para>
/// </summary>
public class ProductPriceChangedConsumer : IConsumer<ProductPriceChangedIntegrationEvent>
{
    /// <summary>
    /// How many baskets are re-priced at once. The multiplexer pipelines their commands over its one connection, so this
    /// bounds the load on Redis, not the number of connections.
    /// </summary>
    internal const int MaxConcurrentBaskets = 16;

    private readonly IBasketRepository _basketRepository;
    private readonly RedisMessageIdempotencyStore _idempotencyStore;
    private readonly PriceChangeWatermark _watermark;
    private readonly ILogger<ProductPriceChangedConsumer> _logger;
    private readonly IBasketMetrics _metrics;

    public ProductPriceChangedConsumer(
        IBasketRepository basketRepository,
        RedisMessageIdempotencyStore idempotencyStore,
        PriceChangeWatermark watermark,
        ILogger<ProductPriceChangedConsumer> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _idempotencyStore = idempotencyStore;
        _watermark = watermark;
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
            if (!await _watermark.TryAdvanceAsync(message.ProductId, message.OccurredOn))
            {
                _logger.LogInformation(
                    "Skipping a price change older than one already applied. ProductId={ProductId}, OccurredOn={OccurredOn}, MessageId={MessageId}",
                    message.ProductId,
                    message.OccurredOn,
                    messageId);

                await _idempotencyStore.TryMarkProcessedAsync(messageId, TimeSpan.FromDays(7));
                _metrics.RecordPriceSyncUpdate("stale");
                return;
            }

            var userIds = await _basketRepository.GetUsersContainingProductAsync(message.ProductId, context.CancellationToken);
            if (userIds.Count == 0)
            {
                await _idempotencyStore.TryMarkProcessedAsync(messageId, TimeSpan.FromDays(7));
                return;
            }

            var parallelism = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxConcurrentBaskets,
                CancellationToken = context.CancellationToken
            };

            await Parallel.ForEachAsync(userIds, parallelism, async (userId, cancellationToken) =>
            {
                var outcome = await BasketWrites.RunAsync(
                    ct => RepriceAsync(userId, message, ct),
                    cancellationToken);

                if (outcome.IsFailure)
                {
                    // Not marked processed, so the message is redelivered and the whole fan-out re-run; re-pricing a
                    // basket that already has the new price writes nothing.
                    throw new InvalidOperationException(
                        $"The basket of user '{userId}' kept changing while ProductId={message.ProductId} was re-priced.");
                }
            });

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

        // After the read (see the class comment): a newer change may have reached this basket since this event started.
        if (await _watermark.IsSupersededAsync(message.ProductId, message.OccurredOn))
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
