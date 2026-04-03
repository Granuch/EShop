using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;
using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Application.Orders.Queries;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

[TestFixture]
public class GetOrdersByUserQueryHandlerTests
{
    private Mock<IOrderQueryService> _orderQueryServiceMock = null!;
    private GetOrdersByUserQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderQueryServiceMock = new Mock<IOrderQueryService>();
        _handler = new GetOrdersByUserQueryHandler(_orderQueryServiceMock.Object);
    }

    [Test]
    public async Task Handle_WithExistingOrders_ShouldReturnOrderDtos()
    {
        // Arrange
        var dtos = new List<OrderDto>
        {
            new() { Id = Guid.NewGuid(), UserId = "user-1" },
            new() { Id = Guid.NewGuid(), UserId = "user-1" }
        };

        _orderQueryServiceMock
            .Setup(x => x.GetOrdersByUserAsync("user-1", 1, 10, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((dtos, dtos.Count));

        var query = new GetOrdersByUserQuery { UserId = "user-1" };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Items.Count(), Is.EqualTo(2));
        Assert.That(result.Value.TotalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task Handle_WithNoOrders_ShouldReturnEmptyList()
    {
        // Arrange
        _orderQueryServiceMock
            .Setup(x => x.GetOrdersByUserAsync("user-1", 1, 10, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<OrderDto>(), 0));

        var query = new GetOrdersByUserQuery { UserId = "user-1" };

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Items, Is.Empty);
        Assert.That(result.Value.TotalCount, Is.EqualTo(0));
    }
}
