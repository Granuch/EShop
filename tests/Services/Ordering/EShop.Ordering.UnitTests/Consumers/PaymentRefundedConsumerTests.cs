using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using EShop.Ordering.Infrastructure.Consumers;
using EShop.Ordering.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OrderEntity = EShop.Ordering.Domain.Entities.Order;

namespace EShop.Ordering.UnitTests.Consumers;

/// <summary>
/// Ordering audit Stage 11 (L2): a refund made in Payment now reaches the order. Until then nothing in
/// Ordering consumed <c>PaymentRefundedEvent</c>, so a refunded order went on saying Paid or Shipped.
/// </summary>
[TestFixture]
public class PaymentRefundedConsumerTests
{
    private OrderingDbContext _dbContext = null!;
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private CacheSpy _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContext = new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"PaymentRefundedTests_{Guid.NewGuid()}")
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

    private PaymentRefundedConsumer Consumer() => new(
        _dbContext,
        _orderRepositoryMock.Object,
        _unitOfWorkMock.Object,
        _cache.Invalidator,
        Mock.Of<ILogger<PaymentRefundedConsumer>>());

    private OrderEntity Stored(OrderEntity order)
    {
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        return order;
    }

    private static PaymentRefundedEvent RefundFor(OrderEntity order) => new()
    {
        OrderId = order.Id,
        UserId = order.UserId,
        PaymentIntentId = "pi_refunded",
        Amount = order.TotalPrice,
        RefundedAt = DateTime.UtcNow
    };

    private static OrderEntity CreatePendingOrder()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget", 29.99m, 1) };
        return OrderEntity.Create("user-1", address, items);
    }

    private static OrderEntity CreatePaidOrder()
    {
        var order = CreatePendingOrder();
        order.MarkAsPaid("pi_refunded", order.TotalPrice);
        return order;
    }

    [Test]
    public async Task Consume_ForAPaidOrder_MarksItRefunded_AndInvalidatesItsCaches()
    {
        var order = Stored(CreatePaidOrder());

        await Consumer().Consume(PaymentSuccessConsumerTests.ContextFor(RefundFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Refunded));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.VerifyInvalidated(order);
    }

    [Test]
    public async Task Consume_ForAShippedOrder_MarksItRefunded()
    {
        var order = CreatePaidOrder();
        order.Ship();
        Stored(order);

        await Consumer().Consume(PaymentSuccessConsumerTests.ContextFor(RefundFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Refunded));
    }

    /// <summary>
    /// The refund can overtake the payment's own success. The success that follows must then leave the
    /// order Refunded rather than mark it Paid.
    /// </summary>
    [Test]
    public async Task Consume_ForAPendingOrder_MarksItRefunded_AndALateSuccessLeavesItRefunded()
    {
        var order = Stored(CreatePendingOrder());

        await Consumer().Consume(PaymentSuccessConsumerTests.ContextFor(RefundFor(order)).Object);

        var successConsumer = new PaymentSuccessConsumer(
            _dbContext,
            _orderRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cache.Invalidator,
            Options.Create(new PaymentSuccessConsumer.PaymentSuccessProcessingOptions()),
            Mock.Of<ILogger<PaymentSuccessConsumer>>());
        await successConsumer.Consume(PaymentSuccessConsumerTests.ContextFor(new PaymentSuccessEvent
        {
            OrderId = order.Id,
            PaymentIntentId = "pi_refunded",
            Amount = order.TotalPrice,
            ProcessedAt = DateTime.UtcNow
        }).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Refunded));
    }

    [Test]
    public async Task Consume_ForACancelledOrder_LeavesItCancelled()
    {
        var order = CreatePendingOrder();
        order.Cancel("Changed my mind");
        Stored(order);

        await Consumer().Consume(PaymentSuccessConsumerTests.ContextFor(RefundFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _cache.VerifyNothingInvalidated();
    }

    [Test]
    public async Task Consume_ForAnAlreadyRefundedOrder_IsASkippedDuplicate()
    {
        var order = CreatePaidOrder();
        order.Refund();
        Stored(order);

        await Consumer().Consume(PaymentSuccessConsumerTests.ContextFor(RefundFor(order)).Object);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Refunded));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _cache.VerifyNothingInvalidated();
    }

    [Test]
    public async Task Consume_ForAMissingOrder_DoesNotThrow()
    {
        var message = new PaymentRefundedEvent { OrderId = Guid.NewGuid(), PaymentIntentId = "pi_x", Amount = 1m };
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(message.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderEntity?)null);

        await Consumer().Consume(PaymentSuccessConsumerTests.ContextFor(message).Object);

        _orderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<OrderEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.VerifyNothingInvalidated();
    }
}
