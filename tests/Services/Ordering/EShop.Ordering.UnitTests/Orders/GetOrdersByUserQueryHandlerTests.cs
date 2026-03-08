using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

[TestFixture]
public class GetOrdersByUserQueryHandlerTests
{
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private GetOrdersByUserQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _handler = new GetOrdersByUserQueryHandler(_orderRepositoryMock.Object);
    }

    [Test]
    public async Task Handle_WithExistingOrders_ShouldReturnOrderDtos()
    {
        // Arrange
        var orders = new List<Order>
        {
            CreateOrder("user-1"),
            CreateOrder("user-1")
        };

        _orderRepositoryMock
            .Setup(x => x.GetByUserIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        var query = new GetOrdersByUserQuery { UserId = "user-1" };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Handle_WithNoOrders_ShouldReturnEmptyList()
    {
        // Arrange
        _orderRepositoryMock
            .Setup(x => x.GetByUserIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Order>());

        var query = new GetOrdersByUserQuery { UserId = "user-1" };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Empty);
    }

    private static Order CreateOrder(string userId)
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget", 10.00m, 1) };
        return Order.Create(userId, address, items);
    }
}
