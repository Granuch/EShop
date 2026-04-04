using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.Basket.Application.Common;
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
    public async Task Handle_WhenCompletedCheckoutExists_ShouldReturnDeduplicatedSuccess()
    {
        var checkoutId = Guid.NewGuid();

        var repository = new Mock<IBasketRepository>(MockBehavior.Strict);
        var idempotencyStore = new Mock<ICheckoutIdempotencyStore>();
        idempotencyStore
            .Setup(x => x.GetCompletedCheckoutIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkoutId);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var logger = new Mock<ILogger<CheckoutBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();

        metrics
            .Setup(x => x.MeasureOperation(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());

        var handler = new CheckoutBasketCommandHandler(
            repository.Object,
            idempotencyStore.Object,
            mediator.Object,
            logger.Object,
            metrics.Object);

        var result = await handler.Handle(new CheckoutBasketCommand
        {
            UserId = "user-1",
            ShippingAddress = "Street",
            PaymentMethod = "Card"
        }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(checkoutId));
        metrics.Verify(x => x.RecordCheckout("deduplicated"), Times.Once);
        idempotencyStore.Verify(
            x => x.ReleaseProcessingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task Handle_WhenCheckoutLockNotAcquired_ShouldReturnInProgressFailure()
    {
        var repository = new Mock<IBasketRepository>(MockBehavior.Strict);
        var idempotencyStore = new Mock<ICheckoutIdempotencyStore>();
        idempotencyStore
            .Setup(x => x.GetCompletedCheckoutIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        idempotencyStore
            .Setup(x => x.TryBeginProcessingAsync("user-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        idempotencyStore
            .Setup(x => x.GetCompletedCheckoutIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var logger = new Mock<ILogger<CheckoutBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics
            .Setup(x => x.MeasureOperation(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());

        var handler = new CheckoutBasketCommandHandler(
            repository.Object,
            idempotencyStore.Object,
            mediator.Object,
            logger.Object,
            metrics.Object);

        var result = await handler.Handle(new CheckoutBasketCommand
        {
            UserId = "user-1",
            ShippingAddress = "Street",
            PaymentMethod = "Card"
        }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(BasketErrors.CheckoutAlreadyInProgress));
        metrics.Verify(x => x.RecordCheckout("in_progress"), Times.Once);
    }

    [Test]
    public async Task Handle_WhenBasketIsEmpty_ShouldReturnFailure()
    {
        var repository = new Mock<IBasketRepository>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingBasket?)null);

        var idempotencyStore = new Mock<ICheckoutIdempotencyStore>();
        idempotencyStore
            .Setup(x => x.GetCompletedCheckoutIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        idempotencyStore
            .Setup(x => x.TryBeginProcessingAsync("user-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        idempotencyStore
            .Setup(x => x.ReleaseProcessingAsync("user-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mediator = new Mock<IMediator>();
        var logger = new Mock<ILogger<CheckoutBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();

        metrics
            .Setup(x => x.MeasureOperation(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());

        var handler = new CheckoutBasketCommandHandler(
            repository.Object,
            idempotencyStore.Object,
            mediator.Object,
            logger.Object,
            metrics.Object);

        var result = await handler.Handle(new CheckoutBasketCommand
        {
            UserId = "user-1",
            ShippingAddress = "Street",
            PaymentMethod = "Card"
        }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        mediator.Verify(x => x.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        idempotencyStore.Verify(x => x.ReleaseProcessingAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenDeleteBasketFailsAfterPublish_ShouldReturnSuccess()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Product", 10m, 1);

        var repository = new Mock<IBasketRepository>();
        repository
            .Setup(x => x.GetBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(basket);
        repository
            .Setup(x => x.DeleteBasketAsync("user-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("delete failed"));

        var idempotencyStore = new Mock<ICheckoutIdempotencyStore>();
        idempotencyStore
            .Setup(x => x.GetCompletedCheckoutIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        idempotencyStore
            .Setup(x => x.TryBeginProcessingAsync("user-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        idempotencyStore
            .Setup(x => x.MarkCompletedAsync("user-1", It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        idempotencyStore
            .Setup(x => x.ReleaseProcessingAsync("user-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(x => x.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = new Mock<ILogger<CheckoutBasketCommandHandler>>();
        var metrics = new Mock<IBasketMetrics>();
        metrics
            .Setup(x => x.MeasureOperation(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());

        var handler = new CheckoutBasketCommandHandler(
            repository.Object,
            idempotencyStore.Object,
            mediator.Object,
            logger.Object,
            metrics.Object);

        var result = await handler.Handle(new CheckoutBasketCommand
        {
            UserId = "user-1",
            ShippingAddress = "Street",
            PaymentMethod = "Card"
        }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        idempotencyStore.Verify(
            x => x.MarkCompletedAsync("user-1", It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
        idempotencyStore.Verify(x => x.ReleaseProcessingAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
        metrics.Verify(x => x.RecordCheckout("success"), Times.Once);
    }
}
