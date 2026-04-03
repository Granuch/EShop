using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.ClearBasket;
using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class ClearBasketCommandHandlerTests
{
    [Test]
    public async Task Handle_WhenDeleteSucceeds_ShouldReturnSuccess()
    {
        var repository = new Mock<IBasketRepository>();
        repository.Setup(x => x.DeleteBasketAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var logger = new Mock<ILogger<ClearBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new ClearBasketCommandHandler(repository.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new ClearBasketCommand { UserId = "user-1" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        repository.Verify(x => x.DeleteBasketAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenDeleteThrows_ShouldReturnFailure()
    {
        var repository = new Mock<IBasketRepository>();
        repository.Setup(x => x.DeleteBasketAsync("user-1", It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("redis"));

        var logger = new Mock<ILogger<ClearBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics.Setup(x => x.MeasureOperation(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var handler = new ClearBasketCommandHandler(repository.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new ClearBasketCommand { UserId = "user-1" }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(BasketErrors.BasketOperationFailed));
    }
}
