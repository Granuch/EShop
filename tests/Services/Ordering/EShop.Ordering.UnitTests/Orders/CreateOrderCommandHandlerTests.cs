using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

[TestFixture]
public class CreateOrderCommandHandlerTests
{
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private CreateOrderCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _handler = new CreateOrderCommandHandler(
            _orderRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Test]
    public async Task Handle_WithValidCommand_ShouldReturnSuccessWithOrderId()
    {
        // Arrange
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            State = "IL",
            ZipCode = "62701",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget A", Price = 10.00m, Quantity = 2 }
            }
        };

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.EqualTo(Guid.Empty));
        _orderRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithMultipleItems_ShouldCreateOrderWithAllItems()
    {
        // Arrange
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            State = "IL",
            ZipCode = "62701",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget A", Price = 10.00m, Quantity = 2 },
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget B", Price = 25.50m, Quantity = 1 }
            }
        };

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _orderRepositoryMock.Verify(x => x.AddAsync(
            It.Is<Order>(o => o.Items.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
