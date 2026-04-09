using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using EShop.Ordering.Infrastructure.Consumers;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace EShop.Ordering.UnitTests.Consumers;

[TestFixture]
public class PaymentSuccessConsumerTests
{
    private OrderingDbContext _dbContext = null!;
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<IDistributedCache> _cacheMock = null!;
    private PaymentSuccessConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"PaymentSuccessTests_{Guid.NewGuid()}")
            .Options;
        _dbContext = new OrderingDbContext(options);
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _cacheMock = new Mock<IDistributedCache>();

        var cachingOptions = Options.Create(new CachingBehaviorOptions
        {
            KeyPrefix = "ordering:",
            Version = "v1",
            UseVersioning = true
        });

        _consumer = new PaymentSuccessConsumer(
            _dbContext,
            _cacheMock.Object,
            _orderRepositoryMock.Object,
            _unitOfWorkMock.Object,
            cachingOptions,
            Mock.Of<ILogger<PaymentSuccessConsumer>>());
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task Consume_WithExistingOrder_ShouldMarkAsPaidAndShipOrder()
    {
        // Arrange
        var order = CreatePendingOrder();
        var message = new PaymentSuccessEvent
        {
            OrderId = order.Id,
            PaymentIntentId = "pi_test_123",
            Amount = 29.99m,
            ProcessedAt = DateTime.UtcNow
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context.Object);

        // Assert
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
        Assert.That(order.PaymentIntentId, Is.EqualTo("pi_test_123"));
        Assert.That(order.ShippedAt, Is.Not.Null);
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.RemoveAsync(
            "ordering:v1:orders:user:user-1:p=1:ps=5:cur=",
            It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.RemoveAsync(
            "ordering:v1:orders:user:user-1:p=1:ps=10:cur=",
            It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.RemoveAsync(
            "ordering:v1:orders:user:user-1:p=1:ps=20:cur=",
            It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.RemoveAsync(
            "ordering:v1:orders:user:user-1:p=1:ps=25:cur=",
            It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(x => x.RemoveAsync(
            "ordering:v1:orders:user:user-1:p=1:ps=50:cur=",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Consume_WithNonExistentOrder_ShouldNotThrow()
    {
        // Arrange
        var message = new PaymentSuccessEvent
        {
            OrderId = Guid.NewGuid(),
            PaymentIntentId = "pi_test_456",
            Amount = 10.00m,
            ProcessedAt = DateTime.UtcNow
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(message.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var context = CreateConsumeContext(message);

        // Act & Assert — should not throw
        await _consumer.Consume(context.Object);

        _orderRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Consume_WithAlreadyPaidOrder_ShouldSkipWithoutFailure()
    {
        // Arrange
        var order = CreatePendingOrder();
        order.MarkAsPaid("pi_existing");

        var message = new PaymentSuccessEvent
        {
            OrderId = order.Id,
            PaymentIntentId = "pi_duplicate",
            Amount = 10m,
            ProcessedAt = DateTime.UtcNow
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context.Object);

        // Assert
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Paid));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Consume_WithAlreadyShippedOrder_ShouldSkipWithoutFailure()
    {
        // Arrange
        var order = CreatePendingOrder();
        order.MarkAsPaid("pi_paid");
        order.Ship();

        var message = new PaymentSuccessEvent
        {
            OrderId = order.Id,
            PaymentIntentId = "pi_duplicate",
            Amount = 10m,
            ProcessedAt = DateTime.UtcNow
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context.Object);

        // Assert
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Consume_WithCancelledOrder_ShouldSkipWithoutFailure()
    {
        // Arrange
        var order = CreatePendingOrder();
        order.Cancel("User requested cancellation");

        var message = new PaymentSuccessEvent
        {
            OrderId = order.Id,
            PaymentIntentId = "pi_late",
            Amount = 10m,
            ProcessedAt = DateTime.UtcNow
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context.Object);

        // Assert
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Order CreatePendingOrder()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget", 29.99m, 1) };
        return Order.Create("user-1", address, items);
    }

    private static Mock<ConsumeContext<T>> CreateConsumeContext<T>(T message) where T : class
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
    private PaymentFailedConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"PaymentFailedTests_{Guid.NewGuid()}")
            .Options;
        _dbContext = new OrderingDbContext(options);
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        _consumer = new PaymentFailedConsumer(
            _dbContext,
            _orderRepositoryMock.Object,
            _unitOfWorkMock.Object,
            Mock.Of<ILogger<PaymentFailedConsumer>>());
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task Consume_WithExistingOrder_ShouldCancelOrder()
    {
        // Arrange
        var order = CreatePendingOrder();
        var message = new PaymentFailedEvent
        {
            OrderId = order.Id,
            Reason = "Insufficient funds",
            FailedAt = DateTime.UtcNow
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context.Object);

        // Assert
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(order.CancellationReason, Does.Contain("Insufficient funds"));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Consume_WithNonExistentOrder_ShouldNotThrow()
    {
        // Arrange
        var message = new PaymentFailedEvent
        {
            OrderId = Guid.NewGuid(),
            Reason = "Card declined",
            FailedAt = DateTime.UtcNow
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(message.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var context = CreateConsumeContext(message);

        // Act & Assert — should not throw
        await _consumer.Consume(context.Object);

        _orderRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Order CreatePendingOrder()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget", 19.99m, 1) };
        return Order.Create("user-1", address, items);
    }

    private static Mock<ConsumeContext<T>> CreateConsumeContext<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.Setup(x => x.Message).Returns(message);
        context.Setup(x => x.MessageId).Returns(Guid.NewGuid());
        context.Setup(x => x.CancellationToken).Returns(CancellationToken.None);
        return context;
    }
}

[TestFixture]
public class BasketCheckedOutConsumerTests
{
    private OrderingDbContext _dbContext = null!;
    private Mock<IMediator> _mediatorMock = null!;
    private BasketCheckedOutConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase($"BasketCheckedOutTests_{Guid.NewGuid()}")
            .Options;
        _dbContext = new OrderingDbContext(options);
        _mediatorMock = new Mock<IMediator>();

        _consumer = new BasketCheckedOutConsumer(
            _dbContext,
            _mediatorMock.Object,
            Mock.Of<ILogger<BasketCheckedOutConsumer>>());
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task Consume_WithValidBasketEvent_ShouldSendCreateOrderCommand()
    {
        // Arrange
        var message = new BasketCheckedOutEvent
        {
            UserId = "user-1",
            TotalPrice = 45.50m,
            ShippingAddress = "123 Main St, Springfield, IL, 62701, US",
            PaymentMethod = "card",
            Items = new List<CheckoutItem>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget A", Price = 20.00m, Quantity = 2 },
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget B", Price = 5.50m, Quantity = 1 }
            }
        };

        var orderId = Guid.NewGuid();
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(orderId));

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context.Object);

        // Assert
        _mediatorMock.Verify(x => x.Send(
            It.Is<CreateOrderCommand>(cmd =>
                cmd.UserId == "user-1" &&
                cmd.Street == "123 Main St" &&
                cmd.City == "Springfield" &&
                cmd.State == "IL" &&
                cmd.ZipCode == "62701" &&
                cmd.Country == "US" &&
                cmd.Items.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Consume_WithPartialAddress_ShouldDefaultMissingParts()
    {
        // Arrange
        var message = new BasketCheckedOutEvent
        {
            UserId = "user-2",
            TotalPrice = 10.00m,
            ShippingAddress = "456 Oak Ave, Portland",
            PaymentMethod = "card",
            Items = new List<CheckoutItem>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Gadget", Price = 10.00m, Quantity = 1 }
            }
        };

        _mediatorMock
            .Setup(x => x.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(Guid.NewGuid()));

        var context = CreateConsumeContext(message);

        // Act
        await _consumer.Consume(context.Object);

        // Assert
        _mediatorMock.Verify(x => x.Send(
            It.Is<CreateOrderCommand>(cmd =>
                cmd.Street == "456 Oak Ave" &&
                cmd.City == "Portland" &&
                cmd.State == "Unknown" &&
                cmd.ZipCode == "00000" &&
                cmd.Country == "Unknown"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<ConsumeContext<T>> CreateConsumeContext<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.Setup(x => x.Message).Returns(message);
        context.Setup(x => x.MessageId).Returns(Guid.NewGuid());
        context.Setup(x => x.CancellationToken).Returns(CancellationToken.None);
        return context;
    }
}
