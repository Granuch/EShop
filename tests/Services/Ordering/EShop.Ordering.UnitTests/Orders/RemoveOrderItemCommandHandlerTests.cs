using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

[TestFixture]
public class RemoveOrderItemCommandHandlerTests
{
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private RemoveOrderItemCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _handler = new RemoveOrderItemCommandHandler(
            _orderRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldRemoveItemAndReturnSuccess()
    {
        // Arrange
        var order = CreateOrderWithMultipleItems();
        var itemToRemove = order.Items.First();
        var command = new RemoveOrderItemCommand
        {
            OrderId = order.Id,
            ItemId = itemToRemove.Id
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
        Assert.That(order.Items, Has.Count.EqualTo(1));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentOrder_ShouldReturnNotFoundError()
    {
        // Arrange
        var command = new RemoveOrderItemCommand
        {
            OrderId = Guid.NewGuid(),
            ItemId = Guid.NewGuid()
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

    private static Order CreateOrderWithMultipleItems()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem>
        {
            new(Guid.NewGuid(), "Widget A", 10.00m, 1),
            new(Guid.NewGuid(), "Widget B", 20.00m, 1)
        };
        return Order.Create("user-1", address, items);
    }
}
