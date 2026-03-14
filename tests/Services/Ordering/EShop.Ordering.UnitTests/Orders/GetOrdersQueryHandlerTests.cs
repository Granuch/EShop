using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Application.Orders.Queries;
using EShop.Ordering.Application.Orders.Queries.GetOrders;
using EShop.Ordering.Domain.Entities;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

[TestFixture]
public class GetOrdersQueryHandlerTests
{
    private Mock<IOrderQueryService> _orderQueryServiceMock = null!;
    private GetOrdersQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderQueryServiceMock = new Mock<IOrderQueryService>();
        _handler = new GetOrdersQueryHandler(_orderQueryServiceMock.Object);
    }

    [Test]
    public async Task Handle_WithDefaultQuery_ShouldReturnPagedResult()
    {
        // Arrange
        var query = new GetOrdersQuery();
        var dtos = new List<OrderDto>
        {
            new() { Id = Guid.NewGuid(), UserId = "user-1", TotalPrice = 20.00m, Status = OrderStatus.Pending }
        };

        _orderQueryServiceMock
            .Setup(x => x.GetOrdersAsync(null, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((dtos, 1));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Items.ToList(), Has.Count.EqualTo(1));
        Assert.That(result.Value.TotalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Handle_WithStatusFilter_ShouldPassStatusToQueryService()
    {
        // Arrange
        var query = new GetOrdersQuery { Status = "Paid" };

        _orderQueryServiceMock
            .Setup(x => x.GetOrdersAsync(OrderStatus.Paid, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<OrderDto>(), 0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _orderQueryServiceMock.Verify(
            x => x.GetOrdersAsync(OrderStatus.Paid, 1, 10, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_EmptyResult_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var query = new GetOrdersQuery();

        _orderQueryServiceMock
            .Setup(x => x.GetOrdersAsync(null, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<OrderDto>(), 0));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Items, Is.Empty);
        Assert.That(result.Value.TotalCount, Is.EqualTo(0));
    }
}
