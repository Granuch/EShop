using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.RemoveBasketItem;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class RemoveBasketItemCommandHandlerTests
{
    private Mock<IBasketRepository> _repository = null!;
    private RemoveBasketItemCommandHandler _handler = null!;

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

        _handler = new RemoveBasketItemCommandHandler(
            _repository.Object, Mock.Of<ILogger<RemoveBasketItemCommandHandler>>(), metrics.Object);
    }

    private static RemoveBasketItemCommand Command(Guid productId) => new() { UserId = "user-1", ProductId = productId };

    [Test]
    public async Task Handle_WhenBasketNotFound_ShouldReturnFailure()
    {
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync((ShoppingBasket?)null);

        var result = await _handler.Handle(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketNotFound));
    }

    [Test]
    public async Task Handle_WhenRemovingLastItem_ShouldDeleteBasket()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Chair", 10m, 1);
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);

        var result = await _handler.Handle(Command(productId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _repository.Verify(x => x.TryDeleteBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(x => x.TrySaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenBasketStillHasItems_ShouldSaveBasket()
    {
        var firstProductId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(firstProductId, "Table", 20m, 1);
        basket.AddItem(Guid.NewGuid(), "Lamp", 5m, 1);
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);

        var result = await _handler.Handle(Command(firstProductId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(basket.Items.Count, Is.EqualTo(1));
        _repository.Verify(x => x.TrySaveBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Basket audit S4 (H3). The last item looked removable, but another write added one meanwhile: the conditional
    /// delete loses, and the retry saves the basket with the other item instead of deleting it.
    /// </summary>
    [Test]
    public async Task ALostDeleteRace_IsRedoneOnAFreshRead_AndKeepsTheOtherItem()
    {
        var productId = Guid.NewGuid();
        var stale = ShoppingBasket.Create("user-1");
        stale.AddItem(productId, "Chair", 10m, 1);
        var fresh = ShoppingBasket.Create("user-1");
        fresh.AddItem(productId, "Chair", 10m, 1);
        fresh.AddItem(Guid.NewGuid(), "Added meanwhile", 5m, 1);
        _repository
            .SetupSequence(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale)
            .ReturnsAsync(fresh);
        _repository
            .Setup(x => x.TryDeleteBasketAsync(stale, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(Command(productId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(fresh.Items.Single().ProductName, Is.EqualTo("Added meanwhile"));
        _repository.Verify(x => x.TrySaveBasketAsync(fresh, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenRepositoryThrows_ShouldReturnFailure()
    {
        _repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("fail"));

        var result = await _handler.Handle(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
    }
}
