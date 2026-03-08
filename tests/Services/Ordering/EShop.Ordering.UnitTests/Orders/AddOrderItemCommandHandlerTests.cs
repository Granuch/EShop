using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

[TestFixture]
public class AddOrderItemCommandHandlerTests
{
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private AddOrderItemCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _handler = new AddOrderItemCommandHandler(
            _orderRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldAddItemAndReturnSuccess()
    {
        // Arrange
        var order = CreatePendingOrder();
        var newProductId = Guid.NewGuid();
        var command = new AddOrderItemCommand
        {
            OrderId = order.Id,
            ProductId = newProductId,
            ProductName = "New Widget",
            UnitPrice = 15.00m,
            Quantity = 2
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(order.Items, Has.Count.EqualTo(2));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentOrder_ShouldReturnNotFoundError()
    {
        // Arrange
        var command = new AddOrderItemCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Widget",
            UnitPrice = 10.00m,
            Quantity = 1
        };

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(command.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotFound"));
    }

    private static Order CreatePendingOrder()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget", 10.00m, 1) };
        return Order.Create("user-1", address, items);
    }
}
