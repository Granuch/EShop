using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.UpdateBasketItemQuantity;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class UpdateBasketItemQuantityCommandHandlerTests
{
    private Mock<IBasketRepository> _repository = null!;
    private UpdateBasketItemQuantityCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IBasketRepository>();
        _repository
            .Setup(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repository
            .Setup(x => x.TryDeleteBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        _handler = new UpdateBasketItemQuantityCommandHandler(
            _repository.Object, Mock.Of<ILogger<UpdateBasketItemQuantityCommandHandler>>(), metrics.Object);
    }

    private static ShoppingBasket BasketWith(Guid productId)
    {
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Monitor", 500m, 1);
        return basket;
    }

    private static UpdateBasketItemQuantityCommand Command(Guid productId, int quantity) => new()
    {
        UserId = "user-1",
        ProductId = productId,
        Quantity = quantity
    };

    [Test]
    public async Task Handle_WhenBasketNotFound_ShouldReturnFailure()
    {
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync((ShoppingBasket?)null);

        var result = await _handler.Handle(Command(Guid.NewGuid(), 2), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketNotFound));
    }

    [Test]
    public async Task Handle_WhenQuantityIsZero_ShouldDeleteBasketIfItBecomesEmpty()
    {
        var productId = Guid.NewGuid();
        var basket = BasketWith(productId);
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);

        var result = await _handler.Handle(Command(productId, 0), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _repository.Verify(x => x.TryDeleteBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenQuantityIsPositive_ShouldSaveBasket()
    {
        var productId = Guid.NewGuid();
        var basket = BasketWith(productId);
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);

        var result = await _handler.Handle(Command(productId, 3), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(basket.Items.Single().Quantity, Is.EqualTo(3));
        _repository.Verify(x => x.TrySaveBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Basket audit S4 (H3): the edit is redone on the basket another write stored, not saved over it.</summary>
    [Test]
    public async Task ALostRace_IsRedoneOnAFreshRead()
    {
        var productId = Guid.NewGuid();
        var fresh = BasketWith(productId);
        fresh.ApplyPriceChange(productId, 450m);
        _repository
            .SetupSequence(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BasketWith(productId))
            .ReturnsAsync(fresh);
        _repository
            .SetupSequence(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        var result = await _handler.Handle(Command(productId, 3), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(fresh.Items.Single().Quantity, Is.EqualTo(3));
        Assert.That(fresh.Items.Single().Price, Is.EqualTo(450m), "the price the other write stored must survive the edit");
        // By reference: ShoppingBasket's equality is its Id, which the stale copy shares.
        _repository.Verify(x => x.TrySaveBasketAsync(
            It.Is<ShoppingBasket>(b => ReferenceEquals(b, fresh)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ABasketThatKeepsChanging_IsAConcurrentUpdate()
    {
        var productId = Guid.NewGuid();
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(() => BasketWith(productId));
        _repository
            .Setup(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(Command(productId, 3), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.ConcurrentUpdate));
    }

    [Test]
    public async Task Handle_WhenRepositoryThrows_ShouldReturnFailure()
    {
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("fail"));

        var result = await _handler.Handle(Command(Guid.NewGuid(), 1), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
    }
}
