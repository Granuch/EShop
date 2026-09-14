using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    private static OrderCreatedConsumer CreateConsumer(
        PaymentDbContext dbContext,
        IPaymentProcessor processor,
        IIntegrationEventOutbox outbox,
        bool stripeEnabled = false) => new(
            dbContext,
            new PaymentRepository(dbContext),
            dbContext,
            processor,
            Options.Create(new StripeSettings { Enabled = stripeEnabled }),
            outbox,
            Mock.Of<ILogger<OrderCreatedConsumer>>());

    private static ConsumeContext<OrderCreatedEvent> Created(Guid orderId, string userId = "user-1", decimal total = 100m)
    {
        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            OrderId = orderId,
            UserId = userId,
            TotalAmount = total
        });
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Test]
    public async Task Consume_WhenPaymentProcessorSucceeds_ShouldPersistSuccessAndPublishEvent()
    {
        await using var dbContext = CreateDbContext();

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Successful("pi_123"));

        var outbox = new Mock<IIntegrationEventOutbox>();
        var consumer = CreateConsumer(dbContext, processor.Object, outbox.Object);

        var orderId = Guid.NewGuid();
        await consumer.Consume(Created(orderId));

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

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Failed("Card declined"));

        var outbox = new Mock<IIntegrationEventOutbox>();
        var consumer = CreateConsumer(dbContext, processor.Object, outbox.Object);

        var orderId = Guid.NewGuid();
        await consumer.Consume(Created(orderId, "user-2"));

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
    [TestCase(PaymentStatus.Cancelled)]
    public async Task Consume_WhenPaymentAlreadyFinalized_ShouldSkipProcessing(PaymentStatus terminalStatus)
    {
        await using var dbContext = CreateDbContext();

        var orderId = Guid.NewGuid();
        await dbContext.PaymentTransactions.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = "user-3",
            Amount = 100m,
            Status = terminalStatus,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var processor = new Mock<IPaymentProcessor>();
        var outbox = new Mock<IIntegrationEventOutbox>();
        var consumer = CreateConsumer(dbContext, processor.Object, outbox.Object);

        await consumer.Consume(Created(orderId, "user-3"));

        processor.Verify(x => x.ProcessPaymentAsync(
            It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        outbox.Verify(x => x.Enqueue(
            It.IsAny<EShop.BuildingBlocks.Messaging.IIntegrationEvent>(), It.IsAny<string?>()), Times.Never);
    }

    #region Payment audit Stage 1 (C1, D1): Stripe:Enabled picks the path

    /// <summary>
    /// With Stripe on, the customer pays at Stripe. The consumer records what the order costs and charges
    /// nothing; before Stage 1 the simulator settled the order here regardless.
    /// </summary>
    [Test]
    public async Task WithStripeEnabled_ANewOrder_IsRecordedPendingForTheCustomerToPay_AndNothingIsCharged()
    {
        await using var dbContext = CreateDbContext();
        var processor = new Mock<IPaymentProcessor>(MockBehavior.Strict);
        var outbox = new Mock<IIntegrationEventOutbox>(MockBehavior.Strict);
        var consumer = CreateConsumer(dbContext, processor.Object, outbox.Object, stripeEnabled: true);

        var orderId = Guid.NewGuid();
        await consumer.Consume(Created(orderId, "user-9", 123.45m));

        var payment = await dbContext.PaymentTransactions.SingleAsync(x => x.OrderId == orderId);
        Assert.Multiple(() =>
        {
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(payment.PaymentMethod, Is.EqualTo(PaymentMethodType.Stripe));
            Assert.That(payment.Amount, Is.EqualTo(123.45m));
            Assert.That(payment.Currency, Is.EqualTo("USD"));
            Assert.That(payment.UserId, Is.EqualTo("user-9"));
            Assert.That(payment.PaymentIntentId, Is.Empty);
        });
        processor.VerifyNoOtherCalls();
        outbox.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The C1 defect: a Stripe payment created by /create-intent before OrderCreatedEvent arrived was
    /// re-processed through the simulator, which replaced the real intent id with a fake one and published
    /// PaymentSuccessEvent, so the order was marked Paid with nothing charged. It must be left alone in both modes.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task AStripePaymentAlreadyInFlight_IsLeftUntouched(bool stripeEnabled)
    {
        await using var dbContext = CreateDbContext();
        var orderId = Guid.NewGuid();
        await dbContext.PaymentTransactions.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = "user-4",
            Amount = 100m,
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = "pi_real",
            StripeStatus = "requires_payment_method",
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var processor = new Mock<IPaymentProcessor>(MockBehavior.Strict);
        var outbox = new Mock<IIntegrationEventOutbox>(MockBehavior.Strict);
        var consumer = CreateConsumer(dbContext, processor.Object, outbox.Object, stripeEnabled);

        await consumer.Consume(Created(orderId, "user-4"));

        var payment = await dbContext.PaymentTransactions.AsNoTracking().SingleAsync(x => x.OrderId == orderId);
        Assert.Multiple(() =>
        {
            Assert.That(payment.PaymentIntentId, Is.EqualTo("pi_real"));
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Processing));
        });
        processor.VerifyNoOtherCalls();
        outbox.VerifyNoOtherCalls();
    }

    /// <summary>A redelivered OrderCreatedEvent (a new message id) must not charge the pending Stripe payment.</summary>
    [Test]
    public async Task WithStripeEnabled_ARedeliveredOrderCreated_LeavesThePendingPaymentAlone()
    {
        await using var dbContext = CreateDbContext();
        var processor = new Mock<IPaymentProcessor>(MockBehavior.Strict);
        var consumer = CreateConsumer(dbContext, processor.Object, Mock.Of<IIntegrationEventOutbox>(), stripeEnabled: true);

        var orderId = Guid.NewGuid();
        await consumer.Consume(Created(orderId));
        await consumer.Consume(Created(orderId));

        var payment = await dbContext.PaymentTransactions.SingleAsync(x => x.OrderId == orderId);
        Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Pending));
        processor.VerifyNoOtherCalls();
    }

    /// <summary>
    /// With Stripe off, a simulated payment the consumer itself left in flight (a retry after a failure on
    /// the in-memory path, which has no transaction to roll it back) is still settled.
    /// </summary>
    [Test]
    public async Task WithStripeDisabled_ASimulatedPaymentLeftInFlight_IsSettledOnRetry()
    {
        await using var dbContext = CreateDbContext();
        var orderId = Guid.NewGuid();
        await dbContext.PaymentTransactions.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = "user-5",
            Amount = 100m,
            PaymentMethod = PaymentMethodType.Mock,
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(orderId, 100m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Successful("pi_mock"));
        var consumer = CreateConsumer(dbContext, processor.Object, Mock.Of<IIntegrationEventOutbox>());

        await consumer.Consume(Created(orderId, "user-5"));

        var payment = await dbContext.PaymentTransactions.SingleAsync(x => x.OrderId == orderId);
        Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Success));
        processor.Verify(x => x.ProcessPaymentAsync(orderId, 100m, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Payment audit Stage 8 (M5): a start is announced once, and every event comes from the record

    /// <summary>
    /// A simulated payment in flight was announced when it started. Resuming it used to send a second
    /// PaymentCreatedEvent, which Notification emails to the customer.
    /// </summary>
    [Test]
    public async Task AResumedSimulatedPayment_IsNotAnnouncedAsStartedAgain()
    {
        await using var dbContext = CreateDbContext();
        var orderId = Guid.NewGuid();
        await dbContext.PaymentTransactions.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            UserId = "user-6",
            Amount = 100m,
            PaymentMethod = PaymentMethodType.Mock,
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(orderId, 100m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Successful("pi_mock"));
        var outbox = new Mock<IIntegrationEventOutbox>();
        var consumer = CreateConsumer(dbContext, processor.Object, outbox.Object);

        await consumer.Consume(Created(orderId, "user-6"));

        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Never);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentSuccessEvent>(), It.IsAny<string?>()), Times.Once);
    }

    /// <summary>
    /// The simulator's success used to carry the incoming message's total and a fresh clock reading, rather than the
    /// record's amount and ProcessedAt.
    /// </summary>
    [Test]
    public async Task TheSimulatorsSuccess_CarriesTheRecordsAmountCurrencyAndTime_AndTheMessagesCorrelation()
    {
        await using var dbContext = CreateDbContext();
        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Successful("pi_sim"));

        var enqueued = new List<(IIntegrationEvent Event, string? CorrelationId)>();
        var outbox = new Mock<IIntegrationEventOutbox>();
        outbox.Setup(x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string?>()))
            .Callback<IIntegrationEvent, string?>((e, c) => enqueued.Add((e, c)));
        var consumer = CreateConsumer(dbContext, processor.Object, outbox.Object);

        var orderId = Guid.NewGuid();
        var context = Created(orderId, "user-7", 55.25m);
        await consumer.Consume(context);

        var payment = await dbContext.PaymentTransactions.SingleAsync(x => x.OrderId == orderId);
        var success = enqueued.Select(e => e.Event).OfType<PaymentSuccessEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(success.ProcessedAt, Is.EqualTo(payment.ProcessedAt));
            Assert.That(success.Amount, Is.EqualTo(55.25m));
            Assert.That(success.Currency, Is.EqualTo("USD"));
            Assert.That(success.PaymentIntentId, Is.EqualTo("pi_sim"));
            Assert.That(enqueued.Select(e => e.CorrelationId), Is.All.EqualTo(context.Message.CorrelationId));
            Assert.That(enqueued.Select(e => e.Event.GetType().Name), Is.EqualTo(new[]
            {
                nameof(PaymentCreatedEvent), nameof(PaymentSuccessEvent), nameof(PaymentCompletedEvent)
            }));
        });
    }

    #endregion
}
