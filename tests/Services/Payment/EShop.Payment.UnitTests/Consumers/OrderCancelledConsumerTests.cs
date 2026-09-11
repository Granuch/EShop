using System.Net;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe;

namespace EShop.Payment.UnitTests.Consumers;

/// <summary>
/// Ordering audit Stage 9: a cancelled order must not be charged, and when its payment cannot be
/// cancelled that must be an error — thrown into the error queue — never a log line.
/// </summary>
[TestFixture]
public class OrderCancelledConsumerTests
{
    private PaymentDbContext _db = null!;
    private Mock<IStripePaymentService> _stripe = null!;
    private OrderCancelledConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"order-cancelled-{Guid.NewGuid():N}")
            .Options);
        _stripe = new Mock<IStripePaymentService>(MockBehavior.Strict);
        _consumer = new OrderCancelledConsumer(
            _db, new PaymentRepository(_db), _db, _stripe.Object, Mock.Of<ILogger<OrderCancelledConsumer>>());
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private async Task<PaymentTransaction> SeedAsync(
        PaymentStatus status, string method = "Stripe", string intentId = "pi_live_1")
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 40m,
            Currency = "USD",
            PaymentMethod = method,
            PaymentIntentId = intentId,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.PaymentTransactions.Add(payment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return payment;
    }

    private static ConsumeContext<OrderCancelledEvent> Cancelled(Guid orderId)
    {
        var context = new Mock<ConsumeContext<OrderCancelledEvent>>();
        context.SetupGet(x => x.Message).Returns(new OrderCancelledEvent
        {
            OrderId = orderId,
            UserId = "user-1",
            Reason = "changed my mind"
        });
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private async Task<PaymentTransaction> StoredAsync(Guid orderId)
    {
        _db.ChangeTracker.Clear();
        return await _db.PaymentTransactions.AsNoTracking().SingleAsync(p => p.OrderId == orderId);
    }

    private int StoredClaims => _db.Set<ProcessedMessage>().Count();

    [TestCase(PaymentStatus.Pending)]
    [TestCase(PaymentStatus.Processing)]
    public async Task ALiveStripeIntent_IsCancelledAtStripe_ThenRecordedCancelled(PaymentStatus status)
    {
        var payment = await SeedAsync(status);
        _stripe.Setup(s => s.CancelPaymentIntentAsync("pi_live_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripePaymentIntentCancelResult("pi_live_1", "canceled"));

        await _consumer.Consume(Cancelled(payment.OrderId));

        _stripe.Verify(s => s.CancelPaymentIntentAsync("pi_live_1", It.IsAny<CancellationToken>()), Times.Once);
        var stored = await StoredAsync(payment.OrderId);
        Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Cancelled));
        Assert.That(stored.StripeStatus, Is.EqualTo("canceled"));
        Assert.That(stored.ErrorMessage, Does.Contain("changed my mind"));
    }

    /// <summary>
    /// The core rule. Stripe refusing because the intent already succeeded means the customer paid for
    /// a cancelled order: thrown, as an ArgumentException so it skips the retries, with the payment
    /// record untouched and the message not claimed — so it sits in the error queue with its payload.
    /// </summary>
    [Test]
    public async Task StripeRefusingBecauseTheIntentSucceeded_IsAnError_NotALogLine()
    {
        var payment = await SeedAsync(PaymentStatus.Processing);
        _stripe.Setup(s => s.CancelPaymentIntentAsync("pi_live_1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PaymentIntentNotCancellableException("pi_live_1", "This PaymentIntent's status is succeeded."));

        var ex = Assert.ThrowsAsync<PaymentCancellationFailedException>(() => _consumer.Consume(Cancelled(payment.OrderId)));

        Assert.That(ex, Is.InstanceOf<ArgumentException>(), "ArgumentException is what the retry policies skip");
        Assert.That(ex!.Message, Does.Contain("pi_live_1").And.Contain("succeeded"));
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Processing));
        Assert.That(StoredClaims, Is.EqualTo(0), "a claimed message would be skipped on redelivery");
    }

    /// <summary>Already captured here, too: no Stripe call can undo it, and it must not pass quietly.</summary>
    [TestCase("Stripe")]
    [TestCase("Mock")]
    public async Task AnAlreadyCapturedPayment_IsAnError(string method)
    {
        var payment = await SeedAsync(PaymentStatus.Success, method);

        Assert.ThrowsAsync<PaymentCancellationFailedException>(() => _consumer.Consume(Cancelled(payment.OrderId)));

        _stripe.VerifyNoOtherCalls();
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Success));
    }

    /// <summary>A Stripe outage is not a refusal: it must be retried, not dead-lettered at once.</summary>
    [Test]
    public async Task ATransientStripeFailure_PropagatesUnwrapped_SoItIsRetried()
    {
        var payment = await SeedAsync(PaymentStatus.Processing);
        _stripe.Setup(s => s.CancelPaymentIntentAsync("pi_live_1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(HttpStatusCode.ServiceUnavailable, new StripeError { Code = "api_error" }, "Stripe is down"));

        var ex = Assert.ThrowsAsync<StripeException>(() => _consumer.Consume(Cancelled(payment.OrderId)));

        Assert.That(ex, Is.Not.InstanceOf<ArgumentException>());
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Processing));
    }

    /// <summary>A mock payment in flight, or a Stripe intent not yet created: nothing at the provider.</summary>
    [TestCase("Mock", "")]
    [TestCase("Stripe", "")]
    public async Task APaymentWithNoIntentYet_IsCancelled_WithoutCallingStripe(string method, string intentId)
    {
        var payment = await SeedAsync(PaymentStatus.Processing, method, intentId);

        await _consumer.Consume(Cancelled(payment.OrderId));

        _stripe.VerifyNoOtherCalls();
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Cancelled));
    }

    [TestCase(PaymentStatus.Failed)]
    [TestCase(PaymentStatus.Refunded)]
    [TestCase(PaymentStatus.Cancelled)]
    public async Task APaymentAlreadyOver_IsLeftAlone(PaymentStatus status)
    {
        var payment = await SeedAsync(status);

        await _consumer.Consume(Cancelled(payment.OrderId));

        _stripe.VerifyNoOtherCalls();
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(status));
    }

    /// <summary>
    /// The two events travel on separate queues, so a cancellation can arrive first. The late
    /// OrderCreatedEvent must then find a final record and charge nothing.
    /// </summary>
    [Test]
    public async Task ACancellationThatOvertakesOrderCreated_MeansTheOrderIsNeverCharged()
    {
        var orderId = Guid.NewGuid();

        await _consumer.Consume(Cancelled(orderId));

        Assert.That((await StoredAsync(orderId)).Status, Is.EqualTo(PaymentStatus.Cancelled));

        var processor = new Mock<IPaymentProcessor>(MockBehavior.Strict);
        var created = new OrderCreatedConsumer(
            _db, new PaymentRepository(_db), _db, processor.Object,
            Mock.Of<IIntegrationEventOutbox>(), Mock.Of<ILogger<OrderCreatedConsumer>>());
        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(new OrderCreatedEvent { OrderId = orderId, UserId = "user-1", TotalAmount = 40m });
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await created.Consume(context.Object);

        processor.VerifyNoOtherCalls();
        Assert.That((await StoredAsync(orderId)).Status, Is.EqualTo(PaymentStatus.Cancelled));
    }
}
