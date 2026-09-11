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
    private Mock<IProductCatalogReader> _catalogMock = null!;
    private CreateOrderCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _catalogMock = new Mock<IProductCatalogReader>();
        _handler = new CreateOrderCommandHandler(
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

    private static CreateOrderCommand CommandFor(params (Guid ProductId, int Quantity)[] lines) => new()
    {
        UserId = "user-1",
        Street = "123 Main St",
        City = "Springfield",
        State = "IL",
        ZipCode = "62701",
        Country = "US",
        Items = lines.Select(l => new CreateOrderItemDto { ProductId = l.ProductId, Quantity = l.Quantity }).ToList()
    };

    [Test]
    public async Task Handle_PricesEveryLineFromCatalog()
    {
        var a = CatalogProduct("Widget A", 10.00m);
        var b = CatalogProduct("Widget B", 25.50m);
        Order? added = null;
        _orderRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((o, _) => added = o);

        var result = await _handler.Handle(CommandFor((a, 2), (b, 1)), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(added!.Id));
        Assert.That(added.TotalPrice, Is.EqualTo(45.50m));
        Assert.That(added.Items.Select(i => i.ProductName), Is.EquivalentTo(new[] { "Widget A", "Widget B" }));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WithAProductCatalogDoesNotKnow_ReturnsProductUnavailable_AndAddsNothing()
    {
        var known = CatalogProduct("Widget", 10.00m);
        var unknown = Guid.NewGuid();

        var result = await _handler.Handle(CommandFor((known, 1), (unknown, 1)), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.ProductUnavailable"));
        Assert.That(result.Error.Message, Does.Contain(unknown.ToString()));
        _orderRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenCatalogIsUnavailable_ReturnsCatalogUnavailable_AndAddsNothing()
    {
        _catalogMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CatalogUnavailableException("down"));

        var result = await _handler.Handle(CommandFor((Guid.NewGuid(), 1)), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Catalog.Unavailable"));
        _orderRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
