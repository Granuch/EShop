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
    private Mock<IProductCatalogReader> _catalogMock = null!;
    private AddOrderItemCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _catalogMock = new Mock<IProductCatalogReader>();
        _handler = new AddOrderItemCommandHandler(
            _orderRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _catalogMock.Object);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private Guid CatalogProduct(string name, decimal price)
    {
        var id = Guid.NewGuid();
        _catalogMock
            .Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProduct(id, name, price));
        return id;
    }

    private void Returns(Order order) => _orderRepositoryMock
        .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(order);

    [Test]
    public async Task Handle_AddsTheLineAtTheCatalogPrice()
    {
        var order = CreatePendingOrder();
        Returns(order);
        var productId = CatalogProduct("New Widget", 15.00m);

        var result = await _handler.Handle(
            new AddOrderItemCommand { OrderId = order.Id, ProductId = productId, Quantity = 2 },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var line = order.Items.Single(i => i.ProductId == productId);
        Assert.That(line.ProductName, Is.EqualTo("New Widget"));
        Assert.That(line.UnitPrice, Is.EqualTo(15.00m));
        Assert.That(order.TotalPrice, Is.EqualTo(40.00m));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithNonExistentOrder_ShouldReturnNotFoundError()
    {
        var command = new AddOrderItemCommand { OrderId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 1 };
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(command.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotFound"));
    }

    [Test]
    public async Task Handle_WithAProductCatalogDoesNotKnow_ReturnsProductUnavailable_AndChangesNothing()
    {
        var order = CreatePendingOrder();
        Returns(order);

        var result = await _handler.Handle(
            new AddOrderItemCommand { OrderId = order.Id, ProductId = Guid.NewGuid(), Quantity = 1 },
            CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.ProductUnavailable"));
        Assert.That(order.Items, Has.Count.EqualTo(1));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithPaidOrder_ShouldReturnNotModifiable_AndKeepTotal()
    {
        var order = CreatePendingOrder();
        order.MarkAsPaid("pi_paid", order.TotalPrice);
        Returns(order);

        var result = await _handler.Handle(
            new AddOrderItemCommand { OrderId = order.Id, ProductId = CatalogProduct("Unpaid Widget", 15.00m), Quantity = 1 },
            CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotModifiable"));
        Assert.That(order.TotalPrice, Is.EqualTo(10.00m));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Order CreatePendingOrder()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var items = new List<OrderItem> { new(Guid.NewGuid(), "Widget", 10.00m, 1) };
        return Order.Create("user-1", address, items);
    }
}
