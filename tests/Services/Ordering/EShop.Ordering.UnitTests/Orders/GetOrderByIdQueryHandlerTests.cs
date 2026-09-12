using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Application.Orders.Queries;
using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

/// <summary>
/// The handler reads through <see cref="IOrderQueryService"/> since audit L5; the projection itself is
/// exercised end to end by the integration suite's <c>GetOrderByIdTests</c>.
/// </summary>
[TestFixture]
public class GetOrderByIdQueryHandlerTests
{
    private Mock<IOrderQueryService> _queryServiceMock = null!;
    private GetOrderByIdQueryHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _queryServiceMock = new Mock<IOrderQueryService>();
        _handler = new GetOrderByIdQueryHandler(_queryServiceMock.Object);
    }

    [Test]
    public async Task Handle_WithExistingOrder_ReturnsTheProjectedOrder()
    {
        var dto = new OrderDto { Id = Guid.NewGuid(), UserId = "user-1" };
        _queryServiceMock
            .Setup(x => x.GetOrderByIdAsync(dto.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _handler.Handle(new GetOrderByIdQuery { OrderId = dto.Id }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.SameAs(dto));
    }

    [Test]
    public async Task Handle_WithNonExistentOrder_ShouldReturnNotFoundError()
    {
        var query = new GetOrderByIdQuery { OrderId = Guid.NewGuid() };
        _queryServiceMock
            .Setup(x => x.GetOrderByIdAsync(query.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderDto?)null);

        var result = await _handler.Handle(query, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotFound"));
    }
}
