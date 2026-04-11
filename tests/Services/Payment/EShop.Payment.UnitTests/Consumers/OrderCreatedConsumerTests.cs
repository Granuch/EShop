using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Consumers;
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

        var outbox = new Mock<IIntegrationEventOutbox>();

        var consumer = new OrderCreatedConsumer(
            dbContext,
            repository,
            dbContext,
            processor.Object,
            outbox.Object,
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
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        var payment = await dbContext.PaymentTransactions.SingleAsync(x => x.OrderId == orderId);
        Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Success));
        Assert.That(payment.PaymentIntentId, Is.EqualTo("pi_123"));

        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentSuccessEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCompletedEvent>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task Consume_WhenPaymentProcessorFails_ShouldPersistFailureAndPublishEvent()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Failed("Card declined"));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var consumer = new OrderCreatedConsumer(
            dbContext,
            repository,
            dbContext,
            processor.Object,
            outbox.Object,
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
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        var payment = await dbContext.PaymentTransactions.SingleAsync(x => x.OrderId == orderId);
        Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Failed));
        Assert.That(payment.ErrorMessage, Is.EqualTo("Card declined"));

        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    [TestCase(PaymentStatus.Success)]
    [TestCase(PaymentStatus.Failed)]
    [TestCase(PaymentStatus.Refunded)]
    public async Task Consume_WhenPaymentAlreadyFinalized_ShouldSkipProcessing(PaymentStatus terminalStatus)
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var orderId = Guid.NewGuid();
        var existingPayment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = "user-3",
            Amount = 100m,
            Status = terminalStatus,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await dbContext.PaymentTransactions.AddAsync(existingPayment);
        await dbContext.SaveChangesAsync();

        var processor = new Mock<IPaymentProcessor>();
        var outbox = new Mock<IIntegrationEventOutbox>();

        var consumer = new OrderCreatedConsumer(
            dbContext,
            repository,
            dbContext,
            processor.Object,
            outbox.Object,
            Mock.Of<ILogger<OrderCreatedConsumer>>());

        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            OrderId = orderId,
            UserId = "user-3",
            TotalAmount = 100m
        };

        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        processor.Verify(x => x.ProcessPaymentAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        outbox.Verify(x => x.Enqueue(
            It.IsAny<EShop.BuildingBlocks.Messaging.IIntegrationEvent>(), It.IsAny<string?>()), Times.Never);
    }
}
