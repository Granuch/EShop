using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

[TestFixture]
public class GetOrderByIdQueryHandlerTests
{
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private GetOrderByIdQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _handler = new GetOrderByIdQueryHandler(_orderRepositoryMock.Object);
    }

    [Test]
    public async Task Handle_WithExistingOrder_ShouldReturnOrderDto()
    {
        // Arrange
        var order = CreateOrder();
        var query = new GetOrderByIdQuery { OrderId = order.Id };

        _orderRepositoryMock
            .Setup(x => x.GetByIdReadOnlyAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Id, Is.EqualTo(order.Id));
        Assert.That(result.Value.UserId, Is.EqualTo("user-1"));
        Assert.That(result.Value.Items, Has.Count.EqualTo(1));
        Assert.That(result.Value.ShippingAddress.Street, Is.EqualTo("123 Main St"));
    }

    [Test]
    public async Task Handle_WithNonExistentOrder_ShouldReturnNotFoundError()
    {
        // Arrange
        var query = new GetOrderByIdQuery { OrderId = Guid.NewGuid() };

        _orderRepositoryMock
            .Setup(x => x.GetByIdReadOnlyAsync(query.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotFound"));
    }

    private static Order CreateOrder()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget A", 10.00m, 2) };
        return Order.Create("user-1", address, items);
    }
}
