using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Commands.RefundPayment;
using EShop.Payment.Application.Payments.Refunds;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.Infrastructure.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Ordering audit Stage 19. A refund races Stripe's <c>payment_intent.succeeded</c> webhook. The automatic
/// refund exists precisely because the customer paid while the order was being cancelled, so the success
/// webhook is routinely still on its way when the refund is recorded. When it lands — late, or redelivered —
/// it must not turn the Refunded payment back into Success, and must not publish a PaymentSuccessEvent
/// for money that has been returned.
///
/// <para>Each step runs on its own DbContext over one in-memory database, as the consumer and the webhook
/// run in separate scopes. The two writers meeting at the same instant is resolved by the payment's
/// <c>xmin</c> concurrency token on PostgreSQL, which this in-memory suite cannot exercise; these tests pin
/// the orderings, which are what the status guard decides.</para>
/// </summary>
[TestFixture]
public class LateSuccessWebhookTests
{
    private const string IntentId = "pi_race_1";

    private string _database = null!;
    private Mock<IStripePaymentService> _stripe = null!;

    [SetUp]
    public void SetUp()
    {
        _database = $"late-success-{Guid.NewGuid():N}";
        _stripe = new Mock<IStripePaymentService>();
    }

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseInMemoryDatabase(_database)
        .Options);

    private async Task<PaymentTransaction> SeedAsync(PaymentStatus status)
    {
        await using var db = NewContext();
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 40m,
            Currency = "USD",
            PaymentMethod = "Stripe",
            PaymentIntentId = IntentId,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private async Task<PaymentStatus> StatusAsync(Guid paymentId)
    {
        await using var db = NewContext();
        return (await db.PaymentTransactions.AsNoTracking().SingleAsync(p => p.Id == paymentId)).Status;
    }

    private static PaymentRefunder Refunder(PaymentDbContext db, IStripePaymentService stripe)
        => new(new PaymentRepository(db), Mock.Of<IPaymentProcessor>(), stripe, Mock.Of<IIntegrationEventOutbox>(),
            Mock.Of<ILogger<PaymentRefunder>>());

    /// <summary>Delivers one <c>payment_intent.succeeded</c> webhook through the real processor.</summary>
    private async Task<(StripeWebhookProcessResult Result, Mock<IIntegrationEventOutbox> Outbox)> DeliverSucceededWebhookAsync(string eventId)
    {
        await using var db = NewContext();
        var stripe = new Mock<IStripePaymentService>();
        stripe.Setup(s => s.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent(eventId, "payment_intent.succeeded", IntentId, "succeeded", null, true));
        var outbox = new Mock<IIntegrationEventOutbox>();

        var processor = new StripeWebhookProcessor(
            new PaymentRepository(db), stripe.Object, db, outbox.Object, Mock.Of<ILogger<StripeWebhookProcessor>>());
        var result = await processor.ProcessAsync("payload", "sig", CancellationToken.None);
        return (result, outbox);
    }

    private static void VerifyNoSuccessPublished(Mock<IIntegrationEventOutbox> outbox)
    {
        outbox.Verify(o => o.Enqueue(It.IsAny<PaymentSuccessEvent>(), It.IsAny<string?>()), Times.Never);
        outbox.Verify(o => o.Enqueue(It.IsAny<PaymentCompletedEvent>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// The race the automatic refund is for, end to end: the cancel is refused because the intent has just
    /// succeeded, the consumer refunds it, and only then does Stripe's success webhook arrive.
    /// </summary>
    [Test]
    public async Task ALateSuccessWebhook_AfterTheAutomaticRefund_LeavesThePaymentRefunded_AndPublishesNoSuccess()
    {
        var payment = await SeedAsync(PaymentStatus.Processing);
        _stripe.Setup(s => s.CancelPaymentIntentAsync(IntentId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PaymentIntentNotCancellableException(IntentId, "This PaymentIntent's status is succeeded."));
        _stripe.Setup(s => s.GetPaymentIntentStatusAsync(IntentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("succeeded");
        _stripe.Setup(s => s.CreateRefundAsync(IntentId, 40m, "USD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("re_race_1", "succeeded"));

        await using (var db = NewContext())
        {
            var consumer = new OrderCancelledConsumer(
                db, new PaymentRepository(db), db, _stripe.Object, Refunder(db, _stripe.Object),
                Options.Create(new CancelledOrderRefundSettings { AutoRefund = true }),
                Mock.Of<ILogger<OrderCancelledConsumer>>());
            await consumer.Consume(Cancelled(payment.OrderId));
        }
        Assert.That(await StatusAsync(payment.Id), Is.EqualTo(PaymentStatus.Refunded), "precondition: the refund was recorded");

        var (result, outbox) = await DeliverSucceededWebhookAsync("evt_late_success");

        Assert.That(result.IsDuplicate, Is.False, "a new event, processed on its merits");
        Assert.That(await StatusAsync(payment.Id), Is.EqualTo(PaymentStatus.Refunded));
        VerifyNoSuccessPublished(outbox);
    }

    /// <summary>The same guard after an admin refund: a success webhook for a refunded payment changes nothing.</summary>
    [Test]
    public async Task ALateSuccessWebhook_AfterAnAdminRefund_LeavesThePaymentRefunded_AndPublishesNoSuccess()
    {
        var payment = await SeedAsync(PaymentStatus.Success);
        _stripe.Setup(s => s.CreateRefundAsync(IntentId, 40m, "USD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("re_admin_1", "succeeded"));

        await using (var db = NewContext())
        {
            var handler = new RefundPaymentCommandHandler(
                new PaymentRepository(db), Refunder(db, _stripe.Object), db, Mock.Of<ILogger<RefundPaymentCommandHandler>>());
            var refunded = await handler.Handle(new RefundPaymentCommand(payment.Id, null, "admin"), CancellationToken.None);
            Assert.That(refunded.IsSuccess, Is.True, "precondition: the admin refund went through");
        }

        var (_, outbox) = await DeliverSucceededWebhookAsync("evt_after_admin_refund");

        Assert.That(await StatusAsync(payment.Id), Is.EqualTo(PaymentStatus.Refunded));
        VerifyNoSuccessPublished(outbox);
    }

    /// <summary>
    /// Stripe redelivers a webhook it did not see acknowledged. The success recorded before the refund
    /// comes back after it: recognised by its event id and ignored, so it cannot undo the refund either.
    /// </summary>
    [Test]
    public async Task ARedeliveredSuccessWebhook_AfterARefund_IsADuplicate_AndChangesNothing()
    {
        var payment = await SeedAsync(PaymentStatus.Processing);

        var (first, _) = await DeliverSucceededWebhookAsync("evt_success_1");
        Assert.That(first.IsDuplicate, Is.False);
        Assert.That(await StatusAsync(payment.Id), Is.EqualTo(PaymentStatus.Success), "precondition: the first delivery recorded the success");

        _stripe.Setup(s => s.CreateRefundAsync(IntentId, 40m, "USD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("re_1", "succeeded"));
        await using (var db = NewContext())
        {
            var handler = new RefundPaymentCommandHandler(
                new PaymentRepository(db), Refunder(db, _stripe.Object), db, Mock.Of<ILogger<RefundPaymentCommandHandler>>());
            Assert.That((await handler.Handle(new RefundPaymentCommand(payment.Id, null, "admin"), CancellationToken.None)).IsSuccess, Is.True);
        }

        var (redelivered, outbox) = await DeliverSucceededWebhookAsync("evt_success_1");

        Assert.That(redelivered.IsDuplicate, Is.True);
        Assert.That(await StatusAsync(payment.Id), Is.EqualTo(PaymentStatus.Refunded));
        VerifyNoSuccessPublished(outbox);
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
}
