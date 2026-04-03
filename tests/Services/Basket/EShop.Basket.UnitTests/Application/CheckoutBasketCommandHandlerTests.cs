using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using MediatR;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class CheckoutBasketCommandHandlerTests
{
    [Test]
    public async Task Handle_WhenBasketIsEmpty_ShouldReturnFailure()
    {
        var repository = new Mock<IBasketRepository>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingBasket?)null);

        var mediator = new Mock<IMediator>();
        var logger = new Mock<ILogger<CheckoutBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();

        metrics
            .Setup(x => x.MeasureOperation(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());

        var handler = new CheckoutBasketCommandHandler(repository.Object, mediator.Object, logger.Object, metrics.Object);

        var result = await handler.Handle(new CheckoutBasketCommand
        {
            UserId = "user-1",
            ShippingAddress = "Street",
            PaymentMethod = "Card"
        }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        mediator.Verify(x => x.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
