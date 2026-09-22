using System.Linq;
using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.AddItemToBasket;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class AddItemToBasketCommandHandlerTests
{
    private Mock<IBasketRepository> _repository = null!;
    private Mock<IProductCatalogReader> _catalog = null!;
    private Mock<IBasketMetrics> _metrics = null!;
    private AddItemToBasketCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IBasketRepository>();
        _repository
            .Setup(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _catalog = new Mock<IProductCatalogReader>();
        _catalog
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid productId, CancellationToken _) =>
                new ProductCatalogSnapshot(productId, "Phone", 100m, 100, "https://cdn.test/phone.jpg"));

        _metrics = new Mock<IBasketMetrics>();
        _metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        _handler = new AddItemToBasketCommandHandler(
            _repository.Object,
            _catalog.Object,
            Mock.Of<ILogger<AddItemToBasketCommandHandler>>(),
            _metrics.Object);
    }

    private static AddItemToBasketCommand Command(int quantity = 1) => new()
    {
        UserId = "user-1",
        ProductId = Guid.NewGuid(),
        Quantity = quantity
    };

    [Test]
    public async Task Handle_WhenBasketDoesNotExist_ShouldCreateAndSaveBasket()
    {
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync((ShoppingBasket?)null);

        var result = await _handler.Handle(Command(quantity: 2), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _repository.Verify(x => x.TrySaveBasketAsync(
            It.Is<ShoppingBasket>(b => b.UserId == "user-1" && b.Items.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
        _metrics.Verify(x => x.RecordItemAdded("api"), Times.Once);
    }

    [Test]
    public async Task Handle_ShouldCarryTheCatalogsMainImageOntoTheLine()
    {
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync((ShoppingBasket?)null);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _repository.Verify(x => x.TrySaveBasketAsync(
            It.Is<ShoppingBasket>(b => b.Items.Single().MainImageUrl == "https://cdn.test/phone.jpg"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenBasketExists_ShouldAppendItemAndSave()
    {
        var basket = ShoppingBasket.Create("user-1");
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(basket.Items.Count, Is.EqualTo(1));
        _repository.Verify(x => x.TrySaveBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Basket audit S4 (H3): a lost race is redone on a fresh read, so the other write is kept.</summary>
    [Test]
    public async Task ALostRace_IsRedoneOnAFreshRead_WithoutAskingCatalogAgain()
    {
        var stale = ShoppingBasket.Create("user-1");
        var fresh = ShoppingBasket.Create("user-1");
        fresh.AddItem(Guid.NewGuid(), "Added in another tab", 5m, 1);
        _repository
            .SetupSequence(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale)
            .ReturnsAsync(fresh);
        _repository
            .SetupSequence(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(fresh.Items, Has.Count.EqualTo(2), "the item must be added on top of what the other write stored");
        // By reference: ShoppingBasket's equality is its Id, which the stale copy shares.
        _repository.Verify(x => x.TrySaveBasketAsync(
            It.Is<ShoppingBasket>(b => ReferenceEquals(b, fresh)), It.IsAny<CancellationToken>()), Times.Once);
        _catalog.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _metrics.Verify(x => x.RecordItemAdded("api"), Times.Once);
    }

    [Test]
    public async Task ABasketThatKeepsChanging_IsAConcurrentUpdate_AfterMaxAttempts()
    {
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(() => ShoppingBasket.Create("user-1"));
        _repository
            .Setup(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.ConcurrentUpdate));
        _repository.Verify(
            x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()),
            Times.Exactly(BasketWrites.MaxAttempts));
        _metrics.Verify(x => x.RecordItemAdded(It.IsAny<string>()), Times.Never);
    }

    /// <summary>Basket audit S6 (H5): the stock check counts what the line already holds.</summary>
    [Test]
    public async Task AddingBeyondTheStock_CountingWhatIsAlreadyInTheBasket_IsInsufficientStock()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Phone", 100m, 2);
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);
        _catalog
            .Setup(x => x.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductCatalogSnapshot(productId, "Phone", 100m, 3));

        var result = await _handler.Handle(
            new AddItemToBasketCommand { UserId = "user-1", ProductId = productId, Quantity = 2 }, CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.InsufficientStock));
        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenRepositoryThrows_ShouldReturnFailure()
    {
        _repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
        _metrics.Verify(x => x.RecordItemAdded(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenCatalogProductNotFound_ShouldReturnProductNotFound()
    {
        var repository = new Mock<IBasketRepository>(MockBehavior.Strict);
        _catalog
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCatalogSnapshot?)null);
        var handler = new AddItemToBasketCommandHandler(
            repository.Object, _catalog.Object, Mock.Of<ILogger<AddItemToBasketCommandHandler>>(), _metrics.Object);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.ProductNotFound));
    }

    /// <summary>Basket audit S8 (L5): HttpClient's own timeout is a TaskCanceledException, not an HttpRequestException.</summary>
    [Test]
    public async Task ACatalogTimeout_IsProductVerificationFailed()
    {
        _catalog
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timed out"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.ProductVerificationFailed));
    }

    [Test]
    public async Task Handle_WhenCatalogLookupFails_ShouldReturnProductVerificationFailed()
    {
        var repository = new Mock<IBasketRepository>(MockBehavior.Strict);
        _catalog
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("catalog unavailable"));
        var handler = new AddItemToBasketCommandHandler(
            repository.Object, _catalog.Object, Mock.Of<ILogger<AddItemToBasketCommandHandler>>(), _metrics.Object);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.ProductVerificationFailed));
    }
}
