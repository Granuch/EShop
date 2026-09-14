using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Configuration;
using EShop.Basket.Infrastructure.Consumers;
using EShop.Basket.Infrastructure.Idempotency;
using EShop.BuildingBlocks.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class ProductPriceChangedConsumerTests
{
    private static readonly Guid ProductId = Guid.NewGuid();

    private Mock<IBasketRepository> _repository = null!;
    private Mock<RedisMessageIdempotencyStore> _idempotency = null!;
    private Mock<PriceChangeWatermark> _watermark = null!;
    private Mock<IBasketMetrics> _metrics = null!;
    private ProductPriceChangedConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IBasketRepository>();
        _repository
            .Setup(x => x.GetUsersContainingProductAsync(ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["user-1"]);
        _repository
            .Setup(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repository
            .Setup(x => x.TryRemoveFromProductIndexAsync(ProductId, "user-1", It.IsAny<ShoppingBasket?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _idempotency = new Mock<RedisMessageIdempotencyStore>(new Mock<IConnectionMultiplexer>().Object) { CallBase = false };
        _idempotency.Setup(x => x.IsProcessedAsync(It.IsAny<Guid>())).ReturnsAsync(false);
        _idempotency.Setup(x => x.TryBeginProcessingAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
        _idempotency.Setup(x => x.TryMarkProcessedAsync(It.IsAny<Guid>(), TimeSpan.FromDays(7))).ReturnsAsync(true);
        _idempotency.Setup(x => x.CompleteProcessingAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);

        _watermark = new Mock<PriceChangeWatermark>(
            new Mock<IConnectionMultiplexer>().Object, Options.Create(new RedisBasketOptions())) { CallBase = false };
        _watermark.Setup(x => x.TryAdvanceAsync(ProductId, It.IsAny<DateTime>())).ReturnsAsync(true);
        _watermark.Setup(x => x.IsSupersededAsync(ProductId, It.IsAny<DateTime>())).ReturnsAsync(false);

        _metrics = new Mock<IBasketMetrics>();

        _consumer = new ProductPriceChangedConsumer(
            _repository.Object,
            _idempotency.Object,
            _watermark.Object,
            Mock.Of<ILogger<ProductPriceChangedConsumer>>(),
            _metrics.Object);
    }

    private static ShoppingBasket BasketWithProductAt(decimal price)
    {
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(ProductId, "Product", price, 1);
        return basket;
    }

    private void BasketIs(ShoppingBasket? basket)
        => _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);

    private static ConsumeContext<ProductPriceChangedIntegrationEvent> Context(decimal newPrice)
    {
        var context = new Mock<ConsumeContext<ProductPriceChangedIntegrationEvent>>();
        context.SetupGet(x => x.Message).Returns(new ProductPriceChangedIntegrationEvent
        {
            ProductId = ProductId,
            OldPrice = 10m,
            NewPrice = newPrice
        });
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Test]
    public async Task Consume_WhenMessageAlreadyProcessed_ShouldSkipHandling()
    {
        _idempotency.Setup(x => x.IsProcessedAsync(It.IsAny<Guid>())).ReturnsAsync(true);

        await _consumer.Consume(Context(12m));

        _repository.Verify(x => x.GetUsersContainingProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _idempotency.Verify(x => x.TryBeginProcessingAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Test]
    public async Task Consume_WhenProcessingLockNotAcquired_ShouldSkipHandling()
    {
        _idempotency.Setup(x => x.TryBeginProcessingAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>())).ReturnsAsync(false);

        await _consumer.Consume(Context(12m));

        _repository.Verify(x => x.GetUsersContainingProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _idempotency.Verify(x => x.CompleteProcessingAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Test]
    public async Task Consume_WhenProcessingSucceeds_ShouldUpdateBasketsAndMarkProcessed()
    {
        var basket = BasketWithProductAt(10m);
        BasketIs(basket);

        await _consumer.Consume(Context(15m));

        Assert.That(basket.Items.Single().Price, Is.EqualTo(15m));
        _repository.Verify(x => x.TrySaveBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
        _idempotency.Verify(x => x.TryMarkProcessedAsync(It.IsAny<Guid>(), TimeSpan.FromDays(7)), Times.Once);
        _idempotency.Verify(x => x.CompleteProcessingAsync(It.IsAny<Guid>()), Times.Once);
        _metrics.Verify(x => x.RecordPriceSyncUpdate("success"), Times.Once);
    }

    /// <summary>Basket audit S4 (H3): a basket the customer changed during the sync is re-priced on a fresh read.</summary>
    [Test]
    public async Task ALostRace_IsRedoneOnAFreshRead()
    {
        var fresh = BasketWithProductAt(10m);
        fresh.AddItem(Guid.NewGuid(), "Added during the sync", 5m, 1);
        _repository
            .SetupSequence(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BasketWithProductAt(10m))
            .ReturnsAsync(fresh);
        _repository
            .SetupSequence(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        await _consumer.Consume(Context(15m));

        Assert.That(fresh.Items, Has.Count.EqualTo(2));
        Assert.That(fresh.Items.Single(i => i.ProductId == ProductId).Price, Is.EqualTo(15m));
        // By reference: ShoppingBasket's equality is its Id, which the stale copy shares.
        _repository.Verify(x => x.TrySaveBasketAsync(
            It.Is<ShoppingBasket>(b => ReferenceEquals(b, fresh)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ABasketThatKeepsChanging_FailsTheMessage_WithoutMarkingItProcessed()
    {
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(() => BasketWithProductAt(10m));
        _repository
            .Setup(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Assert.ThrowsAsync<InvalidOperationException>(() => _consumer.Consume(Context(15m)));

        _repository.Verify(
            x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()),
            Times.Exactly(BasketWrites.MaxAttempts));
        _idempotency.Verify(x => x.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>()), Times.Never,
            "the redelivery must run the sync again");
    }

    /// <summary>Basket audit M8: a stale reverse-index entry is dropped, not left to cost a read on every price change.</summary>
    [Test]
    public async Task AUserTheIndexStillListsButWhoseBasketIsGone_IsDroppedFromTheIndex()
    {
        BasketIs(null);

        await _consumer.Consume(Context(15m));

        _repository.Verify(x => x.TryRemoveFromProductIndexAsync(ProductId, "user-1", null, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task AUserWhoseBasketNoLongerHoldsTheProduct_IsDroppedFromTheIndex()
    {
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(Guid.NewGuid(), "Something else", 5m, 1);
        BasketIs(basket);

        await _consumer.Consume(Context(15m));

        _repository.Verify(x => x.TryRemoveFromProductIndexAsync(ProductId, "user-1", basket, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ABasketAlreadyAtTheNewPrice_IsNotRewritten()
    {
        BasketIs(BasketWithProductAt(15m));

        await _consumer.Consume(Context(15m));

        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
        _idempotency.Verify(x => x.TryMarkProcessedAsync(It.IsAny<Guid>(), TimeSpan.FromDays(7)), Times.Once);
    }

    /// <summary>Basket audit S9 (M10): a change older than one already applied touches no basket.</summary>
    [Test]
    public async Task AChangeOlderThanOneAlreadyApplied_TouchesNoBasket_AndIsMarkedProcessed()
    {
        _watermark.Setup(x => x.TryAdvanceAsync(ProductId, It.IsAny<DateTime>())).ReturnsAsync(false);
        BasketIs(BasketWithProductAt(10m));

        await _consumer.Consume(Context(15m));

        _repository.Verify(x => x.GetUsersContainingProductAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
        _idempotency.Verify(x => x.TryMarkProcessedAsync(It.IsAny<Guid>(), TimeSpan.FromDays(7)), Times.Once,
            "a redelivery of it would be just as stale");
    }

    /// <summary>Basket audit S9 (M10): a newer change recorded after this one started is not overwritten.</summary>
    [Test]
    public async Task ABasketANewerChangeReachedMeanwhile_IsNotRewritten()
    {
        _watermark.Setup(x => x.IsSupersededAsync(ProductId, It.IsAny<DateTime>())).ReturnsAsync(true);
        BasketIs(BasketWithProductAt(10m));

        await _consumer.Consume(Context(15m));

        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
        _idempotency.Verify(x => x.TryMarkProcessedAsync(It.IsAny<Guid>(), TimeSpan.FromDays(7)), Times.Once);
    }

    private void UsersAre(params string[] userIds)
        => _repository
            .Setup(x => x.GetUsersContainingProductAsync(ProductId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

    /// <summary>
    /// Basket audit S9 (M9): every basket read waits until all three have started, which only a concurrent fan-out can
    /// satisfy. The old sequential loop waits on the first read for ever, so the wait is bounded rather than awaited.
    /// </summary>
    [Test]
    public async Task TheFanOut_RepricesSeveralBasketsAtOnce()
    {
        string[] users = ["user-1", "user-2", "user-3"];
        UsersAre(users);
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<ShoppingBasket?> ReadOnceAllHaveStarted(string userId)
        {
            if (Interlocked.Increment(ref started) == users.Length)
            {
                allStarted.TrySetResult();
            }

            await allStarted.Task;
            var basket = ShoppingBasket.Create(userId);
            basket.AddItem(ProductId, "Product", 10m, 1);
            return basket;
        }

        _repository
            .Setup(x => x.GetBasketAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string userId, CancellationToken _) => ReadOnceAllHaveStarted(userId));

        var consuming = _consumer.Consume(Context(15m));
        var first = await Task.WhenAny(consuming, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.That(first, Is.SameAs(consuming), "the baskets were re-priced one after another");
        await consuming;
        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Test]
    public async Task TheFanOut_RepricesAtMostMaxConcurrentBasketsAtOnce()
    {
        UsersAre(Enumerable.Range(0, ProductPriceChangedConsumer.MaxConcurrentBaskets * 3).Select(i => $"user-{i}").ToArray());
        var inFlight = 0;
        var mostInFlight = 0;

        async Task<ShoppingBasket?> SlowRead(string userId)
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref mostInFlight, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref inFlight);

            var basket = ShoppingBasket.Create(userId);
            basket.AddItem(ProductId, "Product", 10m, 1);
            return basket;
        }

        _repository
            .Setup(x => x.GetBasketAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string userId, CancellationToken _) => SlowRead(userId));

        await _consumer.Consume(Context(15m));

        Assert.That(mostInFlight, Is.InRange(2, ProductPriceChangedConsumer.MaxConcurrentBaskets));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
