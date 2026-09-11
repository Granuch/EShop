using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using EShop.Ordering.Infrastructure.Consumers;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OrderEntity = EShop.Ordering.Domain.Entities.Order;

namespace EShop.Ordering.UnitTests.Consumers;

[TestFixture]
public class PaymentSuccessConsumerTests
{
    private OrderingDbContext _dbContext = null!;
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private CacheSpy _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContext = new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"PaymentSuccessTests_{Guid.NewGuid()}")
            .Options);
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _cache = new CacheSpy();
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    /// <param name="autoShip"><c>null</c> leaves the options at their defaults.</param>
    private PaymentSuccessConsumer Consumer(bool? autoShip = true) => new(
        _dbContext,
        _orderRepositoryMock.Object,
        _unitOfWorkMock.Object,
        _cache.Invalidator,
        Options.Create(autoShip is { } ship
            ? new PaymentSuccessConsumer.PaymentSuccessProcessingOptions { AutoShipOnPaymentSuccess = ship }
            : new PaymentSuccessConsumer.PaymentSuccessProcessingOptions()),
        Mock.Of<ILogger<PaymentSuccessConsumer>>());

    private OrderEntity Stored(OrderEntity order)
    {
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        return order;
    }

    private static PaymentSuccessEvent PaymentFor(OrderEntity order, decimal? amount = null) => new()
    {
        OrderId = order.Id,
        PaymentIntentId = "pi_test_123",
        Amount = amount ?? order.TotalPrice,
        ProcessedAt = DateTime.UtcNow
    };

    [Test]
    public async Task Consume_WithExistingOrder_ShouldMarkAsPaidAndShipOrder()
    {
        var order = Stored(CreatePendingOrder());

        await Consumer(autoShip: true).Consume(ContextFor(PaymentFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
        Assert.That(order.PaymentIntentId, Is.EqualTo("pi_test_123"));
        Assert.That(order.ShippedAt, Is.Not.Null);
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Audit H4/M8. The order's detail entry and its owner's list family are both invalidated, after
    /// commit. This used to SCAN the whole Redis keyspace for list pages and never evict the detail
    /// entry, so GET /orders/{id} kept showing Pending for up to five minutes after payment.
    /// </summary>
    [Test]
    public async Task Consume_InvalidatesTheOrderAndItsOwnersList()
    {
        var order = Stored(CreatePendingOrder());

        await Consumer().Consume(ContextFor(PaymentFor(order)).Object);

        _cache.VerifyInvalidated(order);
    }

    /// <summary>The write has committed; a cache outage must not fail the message into a redelivery.</summary>
    [Test]
    public async Task Consume_WhenTheCacheIsDown_StillSucceeds()
    {
        var order = Stored(CreatePendingOrder());
        _cache.Versions
            .Setup(x => x.BumpVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        await Consumer().Consume(ContextFor(PaymentFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
    }

    [Test]
    public async Task Consume_WithNonExistentOrder_ShouldNotThrow()
    {
        var message = new PaymentSuccessEvent { OrderId = Guid.NewGuid(), PaymentIntentId = "pi_test_456", Amount = 10.00m };
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(message.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);

        await Consumer().Consume(ContextFor(message).Object);

        _orderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.VerifyNothingInvalidated();
    }

    [Test]
    public async Task Consume_WithAlreadyPaidOrder_ShouldSkipWithoutFailure()
    {
        var order = CreatePendingOrder();
        order.MarkAsPaid("pi_existing", order.TotalPrice);
        Stored(order);

        await Consumer().Consume(ContextFor(PaymentFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Paid));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _cache.VerifyNothingInvalidated();
    }

    [Test]
    public async Task Consume_WhenAutoShipDisabled_ShouldMarkAsPaidOnly()
    {
        var order = Stored(CreatePendingOrder());

        await Consumer(autoShip: false).Consume(ContextFor(PaymentFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Paid));
        Assert.That(order.ShippedAt, Is.Null);
    }

    /// <summary>Pins the default: with no configuration, a paid order waits for an admin to ship it.</summary>
    [Test]
    public async Task Consume_WithDefaultOptions_ShouldMarkAsPaidWithoutShipping()
    {
        var order = Stored(CreatePendingOrder());

        await Consumer(autoShip: null).Consume(ContextFor(PaymentFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Paid));
        Assert.That(order.ShippedAt, Is.Null);
    }

    [Test]
    public async Task Consume_WithAlreadyShippedOrder_ShouldSkipWithoutFailure()
    {
        var order = CreatePendingOrder();
        order.MarkAsPaid("pi_paid", order.TotalPrice);
        order.Ship();
        Stored(order);

        await Consumer().Consume(ContextFor(PaymentFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Consume_WithCancelledOrder_ShouldSkipWithoutFailure()
    {
        var order = CreatePendingOrder();
        order.Cancel("User requested cancellation");
        Stored(order);

        await Consumer().Consume(ContextFor(PaymentFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Payment charged the total it was sent at creation. An amount that disagrees with the order must
    /// not mark it paid, and must not be acknowledged either: it throws, so the message retries and
    /// then lands in the error queue for reconciliation.
    /// </summary>
    [Test]
    public void Consume_WhenPaidAmountDiffersFromTotal_ShouldThrowAndLeaveOrderPending()
    {
        var order = Stored(CreatePendingOrder()); // total 29.99

        Assert.ThrowsAsync<DomainException>(() =>
            Consumer().Consume(ContextFor(PaymentFor(order, amount: 19.99m)).Object));

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Pending));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _cache.VerifyNothingInvalidated();
    }

    private static OrderEntity CreatePendingOrder()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget", 29.99m, 1) };
        return OrderEntity.Create("user-1", address, items);
    }

    internal static Mock<ConsumeContext<T>> ContextFor<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.Setup(x => x.Message).Returns(message);
        context.Setup(x => x.MessageId).Returns(Guid.NewGuid());
        context.Setup(x => x.CancellationToken).Returns(CancellationToken.None);
        return context;
    }
}

[TestFixture]
public class PaymentFailedConsumerTests
{
    private OrderingDbContext _dbContext = null!;
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private CacheSpy _cache = null!;
    private PaymentFailedConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContext = new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"PaymentFailedTests_{Guid.NewGuid()}")
            .Options);
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _cache = new CacheSpy();

        _consumer = new PaymentFailedConsumer(
            _dbContext,
            _orderRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cache.Invalidator,
            Mock.Of<ILogger<PaymentFailedConsumer>>());
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    private OrderEntity Stored(OrderEntity order)
    {
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        return order;
    }

    private static PaymentFailedEvent FailureFor(OrderEntity order, string reason = "Insufficient funds") =>
        new() { OrderId = order.Id, Reason = reason, FailedAt = DateTime.UtcNow };

    [Test]
    public async Task Consume_WithExistingOrder_ShouldCancelOrder()
    {
        var order = Stored(CreatePendingOrder());

        await _consumer.Consume(PaymentSuccessConsumerTests.ContextFor(FailureFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(order.CancellationReason, Does.Contain("Insufficient funds"));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>This consumer used to invalidate nothing, so a cancelled order kept reading as Pending.</summary>
    [Test]
    public async Task Consume_InvalidatesTheOrderAndItsOwnersList()
    {
        var order = Stored(CreatePendingOrder());

        await _consumer.Consume(PaymentSuccessConsumerTests.ContextFor(FailureFor(order)).Object);

        _cache.VerifyInvalidated(order);
    }

    [Test]
    public async Task Consume_WithNonExistentOrder_ShouldNotThrow()
    {
        var message = new PaymentFailedEvent { OrderId = Guid.NewGuid(), Reason = "Card declined", FailedAt = DateTime.UtcNow };
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(message.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);

        await _consumer.Consume(PaymentSuccessConsumerTests.ContextFor(message).Object);

        _orderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.VerifyNothingInvalidated();
    }

    /// <summary>
    /// A failure that arrives after a success must not cancel an order that has been paid for.
    /// Previously Order.Cancel accepted it; now it would throw, so the consumer has to skip it.
    /// </summary>
    [Test]
    public async Task Consume_WithPaidOrder_ShouldNotCancel()
    {
        var order = CreatePendingOrder();
        order.MarkAsPaid("pi_paid", order.TotalPrice);
        Stored(order);

        await _consumer.Consume(PaymentSuccessConsumerTests.ContextFor(FailureFor(order, "Late failure")).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Paid));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.VerifyNothingInvalidated();
    }

    /// <summary>A duplicate failure used to throw from Order.Cancel and ride every retry to the error queue.</summary>
    [Test]
    public async Task Consume_WithAlreadyCancelledOrder_ShouldSkipWithoutThrowing()
    {
        var order = CreatePendingOrder();
        order.Cancel("Payment failed: first");
        Stored(order);

        await _consumer.Consume(PaymentSuccessConsumerTests.ContextFor(FailureFor(order, "second")).Object);

        Assert.That(order.CancellationReason, Is.EqualTo("Payment failed: first"));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static OrderEntity CreatePendingOrder()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget", 19.99m, 1) };
        return OrderEntity.Create("user-1", address, items);
    }
}
