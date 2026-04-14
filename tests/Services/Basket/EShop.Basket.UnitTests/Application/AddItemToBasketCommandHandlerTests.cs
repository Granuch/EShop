using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.AddItemToBasket;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class AddItemToBasketCommandHandlerTests
{
    [Test]
    public async Task Handle_WhenBasketDoesNotExist_ShouldCreateAndSaveBasket()
    {
        var repository = new Mock<IBasketRepository>();
        var catalogReader = new Mock<IProductCatalogReader>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingBasket?)null);
        repository
            .Setup(x => x.SaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingBasket basket, CancellationToken _) => basket);
        catalogReader
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid productId, CancellationToken _) => new ProductCatalogSnapshot(productId, "Phone", 100m));

        var logger = new Mock<ILogger<AddItemToBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new AddItemToBasketCommandHandler(repository.Object, catalogReader.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new AddItemToBasketCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = 2
        }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        repository.Verify(x => x.SaveBasketAsync(It.Is<ShoppingBasket>(b => b.UserId == "user-1" && b.Items.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
        metrics.Verify(x => x.RecordItemAdded("api"), Times.Once);
    }

    [Test]
    public async Task Handle_WhenBasketExists_ShouldAppendItemAndSave()
    {
        var basket = ShoppingBasket.Create("user-1");

        var repository = new Mock<IBasketRepository>();
        var catalogReader = new Mock<IProductCatalogReader>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(basket);
        repository
            .Setup(x => x.SaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingBasket saved, CancellationToken _) => saved);
        catalogReader
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid productId, CancellationToken _) => new ProductCatalogSnapshot(productId, "Mouse", 50m));

        var logger = new Mock<ILogger<AddItemToBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new AddItemToBasketCommandHandler(repository.Object, catalogReader.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new AddItemToBasketCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = 1
        }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(basket.Items.Count, Is.EqualTo(1));
        repository.Verify(x => x.SaveBasketAsync(basket, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenRepositoryThrows_ShouldReturnFailure()
    {
        var repository = new Mock<IBasketRepository>();
        var catalogReader = new Mock<IProductCatalogReader>();
        catalogReader
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid productId, CancellationToken _) => new ProductCatalogSnapshot(productId, "Keyboard", 40m));
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var logger = new Mock<ILogger<AddItemToBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new AddItemToBasketCommandHandler(repository.Object, catalogReader.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new AddItemToBasketCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = 1
        }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
        metrics.Verify(x => x.RecordItemAdded(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenCatalogProductNotFound_ShouldReturnProductNotFound()
    {
        var repository = new Mock<IBasketRepository>(MockBehavior.Strict);
        var catalogReader = new Mock<IProductCatalogReader>();
        catalogReader
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductCatalogSnapshot?)null);

        var logger = new Mock<ILogger<AddItemToBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new AddItemToBasketCommandHandler(repository.Object, catalogReader.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new AddItemToBasketCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = 1
        }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(BasketErrors.ProductNotFound));
        repository.Verify(x => x.SaveBasketAsync(It.IsAny<ShoppingBasket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenCatalogLookupFails_ShouldReturnProductVerificationFailed()
    {
        var repository = new Mock<IBasketRepository>(MockBehavior.Strict);
        var catalogReader = new Mock<IProductCatalogReader>();
        catalogReader
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("catalog unavailable"));

        var logger = new Mock<ILogger<AddItemToBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new AddItemToBasketCommandHandler(repository.Object, catalogReader.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new AddItemToBasketCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = 1
        }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(BasketErrors.ProductVerificationFailed));
    }
}
