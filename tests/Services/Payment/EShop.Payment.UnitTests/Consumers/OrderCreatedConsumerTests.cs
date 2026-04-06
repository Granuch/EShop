using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Consumers;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Payment.UnitTests.Consumers;

[TestFixture]
public class OrderCreatedConsumerTests
{
    private static PaymentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PaymentDbContext(options);
    }

    [Test]
    public async Task Consume_WhenPaymentProcessorSucceeds_ShouldPersistSuccessAndPublishEvent()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Successful("pi_123"));

        var publishEndpoint = new Mock<IPublishEndpoint>();

        var consumer = new OrderCreatedConsumer(
            repository,
            dbContext,
            processor.Object,
            publishEndpoint.Object,
            Mock.Of<ILogger<OrderCreatedConsumer>>());

        var orderId = Guid.NewGuid();
        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            OrderId = orderId,
            UserId = "user-1",
            TotalAmount = 100m
        };

        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        var payment = await dbContext.PaymentTransactions.SingleAsync(x => x.OrderId == orderId);
        Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Success));
        Assert.That(payment.PaymentIntentId, Is.EqualTo("pi_123"));

        publishEndpoint.Verify(x => x.Publish(
            It.IsAny<PaymentSuccessEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        publishEndpoint.Verify(x => x.Publish(
            It.IsAny<PaymentCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        publishEndpoint.Verify(x => x.Publish(
            It.IsAny<PaymentCompletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Consume_WhenPaymentProcessorFails_ShouldPersistFailureAndPublishEvent()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Failed("Card declined"));

        var publishEndpoint = new Mock<IPublishEndpoint>();

        var consumer = new OrderCreatedConsumer(
            repository,
            dbContext,
            processor.Object,
            publishEndpoint.Object,
            Mock.Of<ILogger<OrderCreatedConsumer>>());

        var orderId = Guid.NewGuid();
        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            OrderId = orderId,
            UserId = "user-2",
            TotalAmount = 100m
        };

        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        var payment = await dbContext.PaymentTransactions.SingleAsync(x => x.OrderId == orderId);
        Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Failed));
        Assert.That(payment.ErrorMessage, Is.EqualTo("Card declined"));

        publishEndpoint.Verify(x => x.Publish(
            It.IsAny<PaymentFailedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        publishEndpoint.Verify(x => x.Publish(
            It.IsAny<PaymentCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
