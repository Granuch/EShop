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
