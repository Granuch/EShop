using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.UpdateBasketItemQuantity;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class UpdateBasketItemQuantityCommandHandlerTests
{
    [Test]
    public async Task Handle_WhenBasketNotFound_ShouldReturnFailure()
    {
        var repository = new Mock<IBasketRepository>();
        repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync((ShoppingBasket?)null);

        var logger = new Mock<ILogger<UpdateBasketItemQuantityCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new UpdateBasketItemQuantityCommandHandler(repository.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new UpdateBasketItemQuantityCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = 2
        }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketNotFound));
    }

    [Test]
    public async Task Handle_WhenQuantityIsZero_ShouldDeleteBasketIfItBecomesEmpty()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Monitor", 500m, 1);

        var repository = new Mock<IBasketRepository>();
        repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);
        repository.Setup(x => x.DeleteBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var logger = new Mock<ILogger<UpdateBasketItemQuantityCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new UpdateBasketItemQuantityCommandHandler(repository.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new UpdateBasketItemQuantityCommand
        {
            UserId = "user-1",
            ProductId = productId,
            Quantity = 0
        }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        repository.Verify(x => x.DeleteBasketAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.SaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenQuantityIsPositive_ShouldSaveBasket()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Laptop", 1200m, 1);

        var repository = new Mock<IBasketRepository>();
        repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(basket);
        repository.Setup(x => x.SaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>())).ReturnsAsync(basket);

        var logger = new Mock<ILogger<UpdateBasketItemQuantityCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new UpdateBasketItemQuantityCommandHandler(repository.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new UpdateBasketItemQuantityCommand
        {
            UserId = "user-1",
            ProductId = productId,
            Quantity = 3
        }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(basket.Items.Single().Quantity, Is.EqualTo(3));
        repository.Verify(x => x.SaveBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenRepositoryThrows_ShouldReturnFailure()
    {
        var repository = new Mock<IBasketRepository>();
        repository.Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("fail"));

        var logger = new Mock<ILogger<UpdateBasketItemQuantityCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new UpdateBasketItemQuantityCommandHandler(repository.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new UpdateBasketItemQuantityCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = 1
        }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
    }
}
