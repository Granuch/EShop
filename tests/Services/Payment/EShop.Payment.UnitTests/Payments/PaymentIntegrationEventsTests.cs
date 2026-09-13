using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Payment audit Stage 8 (M5). Every payment event is built from the payment record, and the two success events state
/// one fact. Until this stage the publisher tests only counted enqueued events. So a success built from the clock, or
/// from the incoming message, passed them, and the simulator's did exactly that.
/// </summary>
[TestFixture]
public class PaymentIntegrationEventsTests
{
    private static readonly DateTime CreatedAt = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ProcessedAt = new(2026, 1, 1, 9, 5, 0, DateTimeKind.Utc);

    private static PaymentTransaction Payment(PaymentStatus status, string currency = "USD") => new()
    {
        Id = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        UserId = "user-8",
        Amount = 42.50m,
        Currency = currency,
        PaymentMethod = PaymentMethodType.Stripe,
        PaymentIntentId = "pi_events",
        Status = status,
        ErrorMessage = status == PaymentStatus.Failed ? "Stripe payment intent canceled." : null,
        CreatedAt = CreatedAt,
        ProcessedAt = status is PaymentStatus.Pending or PaymentStatus.Processing ? null : ProcessedAt,
        UpdatedAt = ProcessedAt
    };

    /// <summary>
    /// The record's currency is not the events' default. So a builder that forgot to copy it would still produce "USD"
    /// here and go red.
    /// </summary>
    [Test]
    public void ASuccess_IsAnnouncedToOrderingAndNotification_WithTheSameFacts_TakenFromTheRecord()
    {
        var payment = Payment(PaymentStatus.Success, currency: "EUR");
        var outbox = new CapturingOutbox();

        outbox.EnqueuePaymentSucceeded(payment, "corr-1");

        var success = outbox.Single<PaymentSuccessEvent>();
        var completed = outbox.Single<PaymentCompletedEvent>();
        Assert.Multiple(() =>
        {
            Assert.That(outbox.Enqueued, Has.Count.EqualTo(2));

            Assert.That(success.OrderId, Is.EqualTo(payment.OrderId));
            Assert.That(success.PaymentIntentId, Is.EqualTo("pi_events"));
            Assert.That(success.Amount, Is.EqualTo(42.50m));
            Assert.That(success.Currency, Is.EqualTo("EUR"));
            Assert.That(success.ProcessedAt, Is.EqualTo(ProcessedAt));

            Assert.That(completed.OrderId, Is.EqualTo(payment.OrderId));
            Assert.That(completed.UserId, Is.EqualTo("user-8"));
            Assert.That(completed.PaymentIntentId, Is.EqualTo("pi_events"));
            Assert.That(completed.Amount, Is.EqualTo(42.50m));
            Assert.That(completed.Currency, Is.EqualTo("EUR"));
            Assert.That(completed.CompletedAt, Is.EqualTo(ProcessedAt));

            Assert.That(success.CorrelationId, Is.EqualTo("corr-1"));
            Assert.That(completed.CorrelationId, Is.EqualTo("corr-1"));
            Assert.That(outbox.Enqueued.Select(e => e.CorrelationId), Is.All.EqualTo("corr-1"));
        });
    }

    [Test]
    public void AStart_SaysTheAttemptIsProcessing_ForTheRecordedAmount()
    {
        var payment = Payment(PaymentStatus.Processing);
        var outbox = new CapturingOutbox();

        outbox.EnqueuePaymentStarted(payment);

        var started = outbox.Single<PaymentCreatedEvent>();
        Assert.Multiple(() =>
        {
            Assert.That(outbox.Enqueued, Has.Count.EqualTo(1));
            Assert.That(started.OrderId, Is.EqualTo(payment.OrderId));
            Assert.That(started.UserId, Is.EqualTo("user-8"));
            Assert.That(started.Amount, Is.EqualTo(42.50m));
            Assert.That(started.Currency, Is.EqualTo("USD"));
            Assert.That(started.Status, Is.EqualTo("PROCESSING"));
            Assert.That(started.CreatedAt, Is.EqualTo(CreatedAt));
        });
    }

    [Test]
    public void AFailure_CarriesTheRecordedReason_AndTime()
    {
        var payment = Payment(PaymentStatus.Failed);
        var outbox = new CapturingOutbox();

        outbox.EnqueuePaymentFailed(payment);

        var failed = outbox.Single<PaymentFailedEvent>();
        Assert.Multiple(() =>
        {
            Assert.That(failed.OrderId, Is.EqualTo(payment.OrderId));
            Assert.That(failed.UserId, Is.EqualTo("user-8"));
            Assert.That(failed.Reason, Is.EqualTo("Stripe payment intent canceled."));
            Assert.That(failed.FailedAt, Is.EqualTo(ProcessedAt));
        });
    }

    /// <summary>
    /// Payment audit Stage 8b: the refund carries the record's currency too. EUR is not the event's default, so a
    /// builder that forgot to copy it would still produce "USD" here and go red.
    /// </summary>
    [Test]
    public void ARefund_CarriesTheRecordedAmountCurrencyAndTime()
    {
        var payment = Payment(PaymentStatus.Refunded, currency: "EUR");
        var outbox = new CapturingOutbox();

        outbox.EnqueuePaymentRefunded(payment);

        var refunded = outbox.Single<PaymentRefundedEvent>();
        Assert.Multiple(() =>
        {
            Assert.That(refunded.OrderId, Is.EqualTo(payment.OrderId));
            Assert.That(refunded.UserId, Is.EqualTo("user-8"));
            Assert.That(refunded.PaymentIntentId, Is.EqualTo("pi_events"));
            Assert.That(refunded.Amount, Is.EqualTo(42.50m));
            Assert.That(refunded.Currency, Is.EqualTo("EUR"));
            Assert.That(refunded.RefundedAt, Is.EqualTo(ProcessedAt));
        });
    }

    public static IEnumerable<TestCaseData> AnnouncementsTheStatusDoesNotSay()
    {
        foreach (var status in Enum.GetValues<PaymentStatus>())
        {
            if (status != PaymentStatus.Processing)
            {
                yield return new TestCaseData(Started, status);
            }

            if (status != PaymentStatus.Success)
            {
                yield return new TestCaseData(Succeeded, status);
            }

            if (status != PaymentStatus.Failed)
            {
                yield return new TestCaseData(Failed, status);
            }

            if (status != PaymentStatus.Refunded)
            {
                yield return new TestCaseData(Refunded, status);
            }
        }
    }

    /// <summary>A writer that forgot the transition cannot announce what the record does not show.</summary>
    [TestCaseSource(nameof(AnnouncementsTheStatusDoesNotSay))]
    public void AnEvent_IsRefused_ForAPaymentWhoseStatusDoesNotSayIt(string announcement, PaymentStatus status)
    {
        var outbox = new CapturingOutbox();

        Assert.Throws<InvalidOperationException>(() => Announce(outbox, announcement, Payment(status)));
        Assert.That(outbox.Enqueued, Is.Empty);
    }

    [Test]
    public void ASettledPayment_WithNoProcessedAt_IsRefused()
    {
        var payment = Payment(PaymentStatus.Success);
        payment.ProcessedAt = null;
        var outbox = new CapturingOutbox();

        Assert.Throws<InvalidOperationException>(() => outbox.EnqueuePaymentSucceeded(payment));
        Assert.That(outbox.Enqueued, Is.Empty);
    }

    private const string Started = nameof(PaymentIntegrationEvents.EnqueuePaymentStarted);
    private const string Succeeded = nameof(PaymentIntegrationEvents.EnqueuePaymentSucceeded);
    private const string Failed = nameof(PaymentIntegrationEvents.EnqueuePaymentFailed);
    private const string Refunded = nameof(PaymentIntegrationEvents.EnqueuePaymentRefunded);

    private static void Announce(IIntegrationEventOutbox outbox, string announcement, PaymentTransaction payment)
    {
        switch (announcement)
        {
            case Started:
                outbox.EnqueuePaymentStarted(payment);
                break;
            case Succeeded:
                outbox.EnqueuePaymentSucceeded(payment);
                break;
            case Failed:
                outbox.EnqueuePaymentFailed(payment);
                break;
            case Refunded:
                outbox.EnqueuePaymentRefunded(payment);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(announcement), announcement, null);
        }
    }

    private sealed class CapturingOutbox : IIntegrationEventOutbox
    {
        public List<(IIntegrationEvent Event, string? CorrelationId)> Enqueued { get; } = [];

        public void Enqueue(IIntegrationEvent integrationEvent, string? correlationId = null)
            => Enqueued.Add((integrationEvent, correlationId));

        public T Single<T>() => Enqueued.Select(e => e.Event).OfType<T>().Single();
    }
}
