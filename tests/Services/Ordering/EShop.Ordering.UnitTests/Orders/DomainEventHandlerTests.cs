using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Application.Orders.EventHandlers;
using EShop.Ordering.Domain.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

[TestFixture]
public class OrderCreatedDomainEventHandlerTests
{
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private Mock<ILogger<OrderCreatedDomainEventHandler>> _loggerMock = null!;
    private OrderCreatedDomainEventHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _loggerMock = new Mock<ILogger<OrderCreatedDomainEventHandler>>();

        _currentUserContextMock.Setup(x => x.CorrelationId).Returns(Guid.NewGuid().ToString());

        _handler = new OrderCreatedDomainEventHandler(
            _outboxMock.Object,
            _currentUserContextMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task Handle_ShouldEnqueueOrderCreatedIntegrationEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var notification = new OrderCreatedDomainEvent
        {
            OrderId = orderId,
            UserId = "user-1",
            TotalAmount = 45.50m
        };

        // Act
        await _handler.Handle(notification, CancellationToken.None);

        // Assert
        _outboxMock.Verify(x => x.Enqueue(
            It.Is<OrderCreatedEvent>(e =>
                e.OrderId == orderId &&
                e.UserId == "user-1" &&
                e.TotalAmount == 45.50m),
            It.IsAny<string>()), Times.Once);
    }

    /// <summary>
    /// Audit M5: Items was never filled, so Notification's ItemCount was always 0. And the event is
    /// dated when the order was placed, not when the outbox processor got round to this handler.
    /// </summary>
    [Test]
    public async Task Handle_CarriesTheLinesAndTheOrderTime()
    {
        var placedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var productId = Guid.NewGuid();
        var notification = new OrderCreatedDomainEvent
        {
            OccurredOn = placedAt,
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            TotalAmount = 21.00m,
            Items = [new OrderCreatedLine { ProductId = productId, ProductName = "Widget", UnitPrice = 10.50m, Quantity = 2 }]
        };

        OrderCreatedEvent? enqueued = null;
        _outboxMock
            .Setup(x => x.Enqueue(It.IsAny<OrderCreatedEvent>(), It.IsAny<string>()))
            .Callback<object, string>((e, _) => enqueued = (OrderCreatedEvent)e);

        await _handler.Handle(notification, CancellationToken.None);

        Assert.That(enqueued, Is.Not.Null);
        Assert.That(enqueued!.OccurredOn, Is.EqualTo(placedAt));
        Assert.That(enqueued.Currency, Is.EqualTo("USD"), "Notification audit D11: the order confirmation email shows it");
        Assert.That(enqueued.Items, Has.Count.EqualTo(1));
        var item = enqueued.Items[0];
        Assert.That((item.ProductId, item.ProductName, item.Price, item.Quantity, item.SubTotal),
            Is.EqualTo((productId, "Widget", 10.50m, 2, 21.00m)));
    }
}

[TestFixture]
public class OrderPaidDomainEventHandlerTests
{
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private Mock<ILogger<OrderPaidDomainEventHandler>> _loggerMock = null!;
    private OrderPaidDomainEventHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _loggerMock = new Mock<ILogger<OrderPaidDomainEventHandler>>();

        _currentUserContextMock.Setup(x => x.CorrelationId).Returns(Guid.NewGuid().ToString());

        _handler = new OrderPaidDomainEventHandler(
            _outboxMock.Object,
            _currentUserContextMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task Handle_ShouldEnqueueOrderPaidIntegrationEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var notification = new OrderPaidDomainEvent
        {
            OrderId = orderId,
            PaymentIntentId = "pi_123456"
        };

        // Act
        await _handler.Handle(notification, CancellationToken.None);

        // Assert
        _outboxMock.Verify(x => x.Enqueue(
            It.Is<OrderPaidEvent>(e =>
                e.OrderId == orderId &&
                e.PaymentIntentId == "pi_123456"),
            It.IsAny<string>()), Times.Once);
    }
}

[TestFixture]
public class OrderCancelledDomainEventHandlerTests
{
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private Mock<ILogger<OrderCancelledDomainEventHandler>> _loggerMock = null!;
    private OrderCancelledDomainEventHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _loggerMock = new Mock<ILogger<OrderCancelledDomainEventHandler>>();

        _currentUserContextMock.Setup(x => x.CorrelationId).Returns(Guid.NewGuid().ToString());

        _handler = new OrderCancelledDomainEventHandler(
            _outboxMock.Object,
            _currentUserContextMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task Handle_ShouldEnqueueOrderCancelledIntegrationEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var notification = new OrderCancelledDomainEvent
        {
            OrderId = orderId,
            UserId = "user-1",
            Reason = "Changed my mind"
        };

        // Act
        await _handler.Handle(notification, CancellationToken.None);

        // Assert
        _outboxMock.Verify(x => x.Enqueue(
            It.Is<OrderCancelledEvent>(e =>
                e.OrderId == orderId &&
                e.UserId == "user-1" &&
                e.Reason == "Changed my mind"),
            It.IsAny<string>()), Times.Once);
    }
}

[TestFixture]
public class OrderShippedDomainEventHandlerTests
{
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private Mock<ILogger<OrderShippedDomainEventHandler>> _loggerMock = null!;
    private OrderShippedDomainEventHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _loggerMock = new Mock<ILogger<OrderShippedDomainEventHandler>>();

        _currentUserContextMock.Setup(x => x.CorrelationId).Returns(Guid.NewGuid().ToString());

        _handler = new OrderShippedDomainEventHandler(
            _outboxMock.Object,
            _currentUserContextMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task Handle_ShouldEnqueueOrderShippedIntegrationEvent()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var notification = new OrderShippedDomainEvent
        {
            OrderId = orderId,
            UserId = "user-1"
        };

        // Act
        await _handler.Handle(notification, CancellationToken.None);

        // Assert
        _outboxMock.Verify(x => x.Enqueue(
            It.Is<OrderShippedEvent>(e =>
                e.OrderId == orderId &&
                e.UserId == "user-1"),
            It.IsAny<string>()), Times.Once);
    }
}

/// <summary>
/// frontend-contracts F-47. The integration event carries the instant the items changed, not the instant this handler
/// ran in the outbox processor: Payment orders competing totals by it.
/// </summary>
[TestFixture]
public class OrderTotalChangedDomainEventHandlerTests
{
    [Test]
    public async Task Handle_EnqueuesTheNewTotal_AsOfTheChangeItself()
    {
        var outbox = new Mock<IIntegrationEventOutbox>();
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.Setup(x => x.CorrelationId).Returns("corr-47");
        var handler = new OrderTotalChangedDomainEventHandler(
            outbox.Object, currentUser.Object, Mock.Of<ILogger<OrderTotalChangedDomainEventHandler>>());

        OrderTotalChangedEvent? enqueued = null;
        outbox
            .Setup(x => x.Enqueue(It.IsAny<OrderTotalChangedEvent>(), It.IsAny<string>()))
            .Callback<object, string>((e, _) => enqueued = (OrderTotalChangedEvent)e);

        var orderId = Guid.NewGuid();
        var changedAt = new DateTime(2026, 9, 25, 10, 34, 15, DateTimeKind.Utc);

        await handler.Handle(new OrderTotalChangedDomainEvent
        {
            OrderId = orderId,
            UserId = "user-1",
            NewTotal = 675.99m,
            OccurredOn = changedAt
        }, CancellationToken.None);

        Assert.That(enqueued, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(enqueued!.OrderId, Is.EqualTo(orderId));
            Assert.That(enqueued.UserId, Is.EqualTo("user-1"));
            Assert.That(enqueued.NewTotal, Is.EqualTo(675.99m));
            Assert.That(enqueued.Currency, Is.EqualTo("USD"));
            Assert.That(enqueued.TotalAsOf, Is.EqualTo(changedAt), "the change's instant, not the handler's");
            Assert.That(enqueued.CorrelationId, Is.EqualTo("corr-47"));
        });
    }
}
