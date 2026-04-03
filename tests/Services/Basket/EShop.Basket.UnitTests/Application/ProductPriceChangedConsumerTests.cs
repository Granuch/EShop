using EShop.Basket.Application.Abstractions;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Consumers;
using EShop.Basket.Infrastructure.Idempotency;
using EShop.BuildingBlocks.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class ProductPriceChangedConsumerTests
{
    [Test]
    public async Task Consume_WhenMessageAlreadyProcessed_ShouldSkipHandling()
    {
        var basketRepository = new Mock<IBasketRepository>(MockBehavior.Strict);
        var idempotencyStore = CreateIdempotencyStoreMock();
        idempotencyStore
            .Setup(x => x.IsProcessedAsync(It.IsAny<Guid>()))
            .ReturnsAsync(true);

        var metrics = new Mock<IBasketMetrics>(MockBehavior.Strict);
        var logger = new Mock<ILogger<ProductPriceChangedConsumer>>();

        var consumer = new ProductPriceChangedConsumer(
            basketRepository.Object,
            idempotencyStore.Object,
            logger.Object,
            metrics.Object);

        var context = CreateContext(new ProductPriceChangedIntegrationEvent
        {
            ProductId = Guid.NewGuid(),
            OldPrice = 10m,
            NewPrice = 12m
        });

        await consumer.Consume(context.Object);

        basketRepository.Verify(
            x => x.GetUsersContainingProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idempotencyStore.Verify(x => x.TryBeginProcessingAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Test]
    public async Task Consume_WhenProcessingLockNotAcquired_ShouldSkipHandling()
    {
        var basketRepository = new Mock<IBasketRepository>(MockBehavior.Strict);
        var idempotencyStore = CreateIdempotencyStoreMock();
        idempotencyStore
            .Setup(x => x.IsProcessedAsync(It.IsAny<Guid>()))
            .ReturnsAsync(false);
        idempotencyStore
            .Setup(x => x.TryBeginProcessingAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        var metrics = new Mock<IBasketMetrics>(MockBehavior.Strict);
        var logger = new Mock<ILogger<ProductPriceChangedConsumer>>();

        var consumer = new ProductPriceChangedConsumer(
            basketRepository.Object,
            idempotencyStore.Object,
            logger.Object,
            metrics.Object);

        var context = CreateContext(new ProductPriceChangedIntegrationEvent
        {
            ProductId = Guid.NewGuid(),
            OldPrice = 10m,
            NewPrice = 12m
        });

        await consumer.Consume(context.Object);

        basketRepository.Verify(
            x => x.GetUsersContainingProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idempotencyStore.Verify(x => x.CompleteProcessingAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    public async Task Consume_WhenProcessingSucceeds_ShouldUpdateBasketsAndMarkProcessed()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Product", 10m, 1);

        var basketRepository = new Mock<IBasketRepository>();
        basketRepository
            .Setup(x => x.GetUsersContainingProductAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["user-1"]);
        basketRepository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(basket);
        basketRepository
            .Setup(x => x.SaveBasketAsync(basket, It.IsAny<CancellationToken>()))
            .ReturnsAsync(basket);

        var idempotencyStore = CreateIdempotencyStoreMock();
        idempotencyStore
            .Setup(x => x.IsProcessedAsync(It.IsAny<Guid>()))
            .ReturnsAsync(false);
        idempotencyStore
            .Setup(x => x.TryBeginProcessingAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);
        idempotencyStore
            .Setup(x => x.TryMarkProcessedAsync(It.IsAny<Guid>(), TimeSpan.FromDays(7)))
            .ReturnsAsync(true);
        idempotencyStore
            .Setup(x => x.CompleteProcessingAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);

        var metrics = new Mock<IBasketMetrics>();
        var logger = new Mock<ILogger<ProductPriceChangedConsumer>>();

        var consumer = new ProductPriceChangedConsumer(
            basketRepository.Object,
            idempotencyStore.Object,
            logger.Object,
            metrics.Object);

        var context = CreateContext(new ProductPriceChangedIntegrationEvent
        {
            ProductId = productId,
            OldPrice = 10m,
            NewPrice = 15m
        });

        await consumer.Consume(context.Object);

        Assert.That(basket.Items.Single().Price, Is.EqualTo(15m));
        basketRepository.Verify(x => x.SaveBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
        idempotencyStore.Verify(x => x.TryMarkProcessedAsync(It.IsAny<Guid>(), TimeSpan.FromDays(7)), Times.Once);
        idempotencyStore.Verify(x => x.CompleteProcessingAsync(It.IsAny<Guid>()), Times.Once);
        metrics.Verify(x => x.RecordPriceSyncUpdate("success"), Times.Once);
    }

    private static Mock<ConsumeContext<ProductPriceChangedIntegrationEvent>> CreateContext(
        ProductPriceChangedIntegrationEvent message)
    {
        var context = new Mock<ConsumeContext<ProductPriceChangedIntegrationEvent>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static Mock<RedisMessageIdempotencyStore> CreateIdempotencyStoreMock()
    {
        return new Mock<RedisMessageIdempotencyStore>(new Mock<IConnectionMultiplexer>().Object)
        {
            CallBase = false
        };
    }
}
