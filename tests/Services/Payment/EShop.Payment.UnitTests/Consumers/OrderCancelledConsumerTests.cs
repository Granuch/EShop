using System.Net;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Refunds;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private Mock<IPaymentProcessor> _processor = null!;
    private Mock<IIntegrationEventOutbox> _outbox = null!;

    /// <summary>With <c>CancelledOrders:AutoRefund</c> off — the default.</summary>
    private OrderCancelledConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"order-cancelled-{Guid.NewGuid():N}")
            .Options);
        _stripe = new Mock<IStripePaymentService>(MockBehavior.Strict);
        _processor = new Mock<IPaymentProcessor>(MockBehavior.Strict);
        _outbox = new Mock<IIntegrationEventOutbox>();
        _consumer = CreateConsumer(autoRefund: false);
    }

    private OrderCancelledConsumer CreateConsumer(bool autoRefund) => new(
        _db,
        new PaymentRepository(_db),
        _db,
        _stripe.Object,
        new PaymentRefunder(
            new PaymentRepository(_db), _processor.Object, _stripe.Object, _outbox.Object, Mock.Of<ILogger<PaymentRefunder>>()),
        Options.Create(new CancelledOrderRefundSettings { AutoRefund = autoRefund }),
        Mock.Of<ILogger<OrderCancelledConsumer>>());

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

    #region CancelledOrders:AutoRefund on (Ordering audit Stage 19, D16)

    private static PaymentIntentNotCancellableException IntentAlreadySucceeded()
        => new("pi_live_1", "This PaymentIntent's status is succeeded.");

    private void SetupRefund(string method, StripeRefundResult? stripeResult = null)
    {
        if (method == "Stripe")
        {
            _stripe.Setup(s => s.CreateRefundAsync("pi_live_1", 40m, "USD", It.IsAny<CancellationToken>()))
                .ReturnsAsync(stripeResult ?? new StripeRefundResult("re_1", "succeeded"));
        }
        else
        {
            _processor.Setup(p => p.RefundPaymentAsync("pi_live_1", 40m, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PaymentResult.Successful("pi_live_1"));
        }
    }

    private void VerifyRefundAnnounced(Guid orderId, Times times)
        => _outbox.Verify(
            o => o.Enqueue(It.Is<PaymentRefundedEvent>(e => e.OrderId == orderId && e.Amount == 40m), It.IsAny<string?>()),
            times);

    /// <summary>The case the error queue used to get: money captured for an order that no longer exists.</summary>
    [TestCase("Stripe")]
    [TestCase("Mock")]
    public async Task WithAutoRefund_AnAlreadyCapturedPayment_IsRefundedInFull(string method)
    {
        var payment = await SeedAsync(PaymentStatus.Success, method);
        SetupRefund(method);

        await CreateConsumer(autoRefund: true).Consume(Cancelled(payment.OrderId));

        var stored = await StoredAsync(payment.OrderId);
        Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Refunded));
        Assert.That(stored.ErrorMessage, Does.Contain("changed my mind"));
        VerifyRefundAnnounced(payment.OrderId, Times.Once());
        Assert.That(StoredClaims, Is.EqualTo(1), "a settled message is claimed, so a redelivery is skipped");
    }

    /// <summary>
    /// The race: the customer paid while the cancellation was in flight, so Stripe refuses the cancel and
    /// the success webhook has not been recorded yet. Refunded on Stripe's own word that it succeeded.
    /// </summary>
    [Test]
    public async Task WithAutoRefund_AnIntentThatSucceededBeforeItCouldBeCancelled_IsRefunded()
    {
        var payment = await SeedAsync(PaymentStatus.Processing);
        _stripe.Setup(s => s.CancelPaymentIntentAsync("pi_live_1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(IntentAlreadySucceeded());
        _stripe.Setup(s => s.GetPaymentIntentStatusAsync("pi_live_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("succeeded");
        SetupRefund("Stripe");

        await CreateConsumer(autoRefund: true).Consume(Cancelled(payment.OrderId));

        var stored = await StoredAsync(payment.OrderId);
        Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Refunded));
        Assert.That(stored.StripeStatus, Is.EqualTo("succeeded"));
        VerifyRefundAnnounced(payment.OrderId, Times.Once());
    }

    /// <summary>An intent Stripe could not cancel but has not captured either: nothing to refund yet.</summary>
    [TestCase("processing")]
    [TestCase("requires_capture")]
    public async Task WithAutoRefund_AnIntentNotYetSucceededAtStripe_IsStillAnError(string intentStatus)
    {
        var payment = await SeedAsync(PaymentStatus.Processing);
        _stripe.Setup(s => s.CancelPaymentIntentAsync("pi_live_1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(IntentAlreadySucceeded());
        _stripe.Setup(s => s.GetPaymentIntentStatusAsync("pi_live_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(intentStatus);

        Assert.ThrowsAsync<PaymentCancellationFailedException>(
            () => CreateConsumer(autoRefund: true).Consume(Cancelled(payment.OrderId)));

        _stripe.Verify(
            s => s.CreateRefundAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Processing));
        Assert.That(StoredClaims, Is.EqualTo(0));
    }

    /// <summary>A refund Stripe answers and refuses must still reach a person, untouched and unclaimed.</summary>
    [TestCase("failed")]
    [TestCase("canceled")]
    public async Task WithAutoRefund_ARefusedRefund_IsStillAnError_AndChangesNothing(string refundStatus)
    {
        var payment = await SeedAsync(PaymentStatus.Success);
        SetupRefund("Stripe", new StripeRefundResult("re_1", refundStatus));

        var ex = Assert.ThrowsAsync<PaymentCancellationFailedException>(
            () => CreateConsumer(autoRefund: true).Consume(Cancelled(payment.OrderId)));

        Assert.That(ex, Is.InstanceOf<ArgumentException>(), "dead-lettered at once, not retried");
        Assert.That(ex!.Message, Does.Contain("automatic refund was refused").And.Contain(refundStatus));
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Success));
        Assert.That(StoredClaims, Is.EqualTo(0));
        VerifyRefundAnnounced(payment.OrderId, Times.Never());
    }

    /// <summary>Stripe unreachable is not a refusal: retried, never dead-lettered at once.</summary>
    [Test]
    public async Task WithAutoRefund_ATransientRefundFailure_PropagatesUnwrapped_SoItIsRetried()
    {
        var payment = await SeedAsync(PaymentStatus.Success);
        _stripe.Setup(s => s.CreateRefundAsync("pi_live_1", 40m, "USD", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(HttpStatusCode.ServiceUnavailable, new StripeError { Code = "api_error" }, "Stripe is down"));

        var ex = Assert.ThrowsAsync<StripeException>(
            () => CreateConsumer(autoRefund: true).Consume(Cancelled(payment.OrderId)));

        Assert.That(ex, Is.Not.InstanceOf<ArgumentException>());
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Success));
        VerifyRefundAnnounced(payment.OrderId, Times.Never());
    }

    /// <summary>
    /// A retry whose earlier attempt refunded at Stripe but lost its commit. Past the idempotency window
    /// Stripe answers "already refunded" (pinned against the real sandbox in StripeSandboxTests): the
    /// payment is recorded Refunded instead of being dead-lettered as a failure.
    /// </summary>
    [Test]
    public async Task WithAutoRefund_APaymentStripeHasAlreadyRefunded_IsRecordedRefunded()
    {
        var payment = await SeedAsync(PaymentStatus.Success);
        SetupRefund("Stripe", new StripeRefundResult(string.Empty, "succeeded", AlreadyRefunded: true));

        await CreateConsumer(autoRefund: true).Consume(Cancelled(payment.OrderId));

        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Refunded));
        VerifyRefundAnnounced(payment.OrderId, Times.Once());
    }

    /// <summary>A cancellation re-sent from the error queue, or published twice, refunds once.</summary>
    [Test]
    public async Task WithAutoRefund_ACancellationDeliveredTwice_RefundsOnce()
    {
        var payment = await SeedAsync(PaymentStatus.Success);
        SetupRefund("Stripe");
        var consumer = CreateConsumer(autoRefund: true);

        await consumer.Consume(Cancelled(payment.OrderId));
        _db.ChangeTracker.Clear();
        await consumer.Consume(Cancelled(payment.OrderId));

        _stripe.Verify(
            s => s.CreateRefundAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyRefundAnnounced(payment.OrderId, Times.Once());
        Assert.That((await StoredAsync(payment.OrderId)).Status, Is.EqualTo(PaymentStatus.Refunded));
    }

    #endregion
}
