using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.UnitTests.Domain;

/// <summary>
/// Admin panel S11 (endpoint #66): the timeline <see cref="PaymentTransaction"/> writes for itself.
///
/// <para>
/// Every case here asserts a row that only exists because the aggregate wrote it inside the transition. That is the
/// design's whole claim — six different callers move a payment, and a timeline any of them assembled would be missing
/// the other five's work.
/// </para>
/// </summary>
[TestFixture]
public class PaymentEventTimelineTests
{
    private static readonly DateTime Earlier = new(2026, 9, 19, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private static PaymentTransaction A(
        PaymentStatus status,
        PaymentMethodType method = PaymentMethodType.Stripe,
        string intentId = "")
        => new()
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 40m,
            Currency = "USD",
            PaymentMethod = method,
            PaymentIntentId = intentId,
            Status = status,
            CreatedAt = Earlier,
            UpdatedAt = Earlier
        };

    private static PaymentEvent Only(PaymentTransaction payment)
    {
        Assert.That(payment.Events, Has.Count.EqualTo(1), "exactly one row per transition");
        return payment.Events.Single();
    }

    // ---- creation --------------------------------------------------------------------------------------------

    [Test]
    public void ANewPayment_OpensItsTimeline_WithNoPreviousStatus()
    {
        var payment = PaymentTransaction.RecordForOrder(
            Guid.NewGuid(), "user-1", 40m, PaymentMethodType.Stripe, Now);

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.Kind, Is.EqualTo(PaymentEventKind.Transition));
            Assert.That(row.FromStatus, Is.Null, "a payment has no state before it exists");
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(row.OccurredAt, Is.EqualTo(Now));
            Assert.That(row.PaymentTransactionId, Is.EqualTo(payment.Id));
            Assert.That(row.StripeEventId, Is.Null);
        });
    }

    [Test]
    public void ThePlaceholderOfAnOrderCancelledBeforeItsPayment_OpensItsTimelineToo()
    {
        var payment = PaymentTransaction.RecordCancelledBeforeCreation(
            Guid.NewGuid(), "user-1", "the order was cancelled first", Now);

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.FromStatus, Is.Null);
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Cancelled));
            Assert.That(row.Detail, Is.EqualTo("the order was cancelled first"));
        });
    }

    // ---- transitions this service makes ----------------------------------------------------------------------

    [Test]
    public void StartingTheSimulator_RecordsPendingToProcessing()
    {
        var payment = A(PaymentStatus.Pending, PaymentMethodType.Mock);

        payment.StartSimulated(Now);

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.Kind, Is.EqualTo(PaymentEventKind.Transition));
            Assert.That(row.FromStatus, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(row.Detail, Does.Contain("started"));
        });
    }

    /// <summary>A redelivered <c>OrderCreatedEvent</c> resumes a simulated payment; the timeline says so rather than
    /// showing a second start.</summary>
    [Test]
    public void ResumingTheSimulator_SaysSo()
    {
        var payment = A(PaymentStatus.Processing, PaymentMethodType.Mock);

        payment.StartSimulated(Now);

        Assert.That(Only(payment).Detail, Does.Contain("resumed"));
    }

    [Test]
    public void TheSimulatorSettling_RecordsProcessingToSuccess()
    {
        var payment = A(PaymentStatus.Processing, PaymentMethodType.Mock);

        payment.RecordSimulatedSuccess("sim_1", Now);

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.FromStatus, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Success));
        });
    }

    [Test]
    public void TheSimulatorDeclining_RecordsTheReason()
    {
        var payment = A(PaymentStatus.Processing, PaymentMethodType.Mock);

        payment.RecordSimulatedFailure("insufficient funds", Now);

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Failed));
            Assert.That(row.Detail, Does.Contain("insufficient funds"));
        });
    }

    /// <summary>
    /// The operator's reference is on the timeline as well as in <c>PaymentIntentId</c>: that column holds it under an
    /// <c>offline:</c> prefix, where it reads as a provider id rather than as the evidence it is.
    /// </summary>
    [Test]
    public void AnOfflineSettlement_RecordsTheOperatorsReference()
    {
        var payment = A(PaymentStatus.Pending);

        payment.SettleOffline("  TRF-2026-0042  ", Now);

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.Kind, Is.EqualTo(PaymentEventKind.Transition));
            Assert.That(row.FromStatus, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Success));
            Assert.That(row.Detail, Does.Contain("TRF-2026-0042"));
        });
    }

    [Test]
    public void StartingAStripeIntent_RecordsTheIntentId()
    {
        var payment = A(PaymentStatus.Pending);

        payment.StartStripePayment("pi_123", "cus_1", "requires_payment_method", Now);

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.Kind, Is.EqualTo(PaymentEventKind.Transition), "we created the intent, Stripe did not tell us");
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(row.Detail, Does.Contain("pi_123"));
        });
    }

    [Test]
    public void CancellingRecordsTheNote()
    {
        var payment = A(PaymentStatus.Pending);

        payment.Cancel("order cancelled by the customer", Now);

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Cancelled));
            Assert.That(row.Detail, Is.EqualTo("order cancelled by the customer"));
        });
    }

    [Test]
    public void ARefund_AndItsReason_AreTwoRows_InThatOrder()
    {
        var payment = A(PaymentStatus.Success);

        payment.MarkRefunded(Now);
        payment.AnnotateRefund("returned goods, RMA-17");

        Assert.That(payment.Events, Has.Count.EqualTo(2));
        var rows = payment.Events.OrderBy(e => e.OccurredAt).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].FromStatus, Is.EqualTo(PaymentStatus.Success));
            Assert.That(rows[0].ToStatus, Is.EqualTo(PaymentStatus.Refunded));
            Assert.That(rows[1].FromStatus, Is.EqualTo(PaymentStatus.Refunded));
            Assert.That(rows[1].ToStatus, Is.EqualTo(PaymentStatus.Refunded), "an annotation moves nothing");
            Assert.That(rows[1].Detail, Does.Contain("RMA-17"));
        });
    }

    /// <summary>
    /// Both rows above are written in one save, so they routinely ask for the same instant — and ordering the read by
    /// <c>(OccurredAt, Id)</c> would then sort a refund and its reason by two random GUIDs.
    /// </summary>
    [Test]
    public void TwoRowsOfOneOperation_HaveDistinctInstants_InTheOrderTheyHappened()
    {
        var payment = A(PaymentStatus.Success);

        payment.MarkRefunded(Now);
        payment.AnnotateRefund("returned goods");

        var rows = payment.Events.ToList();
        Assert.That(rows[1].OccurredAt, Is.GreaterThan(rows[0].OccurredAt));
    }

    /// <summary>A refused transition throws before it touches anything, so it leaves no row either.</summary>
    [Test]
    public void ARefusedTransition_WritesNoRow()
    {
        var payment = A(PaymentStatus.Success);

        Assert.Throws<DomainException>(() => payment.SettleOffline("TRF-1", Now));

        Assert.That(payment.Events, Is.Empty);
    }

    // ---- Stripe's webhooks -----------------------------------------------------------------------------------

    [Test]
    public void AStripeSuccess_IsAWebhookRow_CarryingTheEventId()
    {
        var payment = A(PaymentStatus.Processing, intentId: "pi_1");

        payment.RecordStripeSuccess("succeeded", Now, "evt_abc");

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.Kind, Is.EqualTo(PaymentEventKind.Webhook));
            Assert.That(row.StripeEventId, Is.EqualTo("evt_abc"));
            Assert.That(row.FromStatus, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Success));
        });
    }

    /// <summary>
    /// The decline is the case the timeline exists for: it changes <c>ErrorMessage</c> and nothing else, and before
    /// this stage the next attempt's success overwrote the only trace that a card had ever been refused.
    /// </summary>
    [Test]
    public void ADeclinedCard_RecordsARow_WithTheStatusUnmoved()
    {
        var payment = A(PaymentStatus.Processing, intentId: "pi_1");

        payment.RecordDeclinedAttempt("Your card was declined.", "requires_payment_method", Now, "evt_decline");

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.Kind, Is.EqualTo(PaymentEventKind.Webhook));
            Assert.That(row.FromStatus, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(row.Detail, Does.Contain("Your card was declined."));
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Processing));
        });
    }

    [Test]
    public void AStripeCancellationRequestedByUs_IsAWebhookRow()
    {
        var payment = A(PaymentStatus.Processing, intentId: "pi_1");

        payment.RecordStripeCancellation(requestedByEShop: true, "canceled", Now, "evt_cancel");

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(row.Kind, Is.EqualTo(PaymentEventKind.Webhook));
            Assert.That(row.StripeEventId, Is.EqualTo("evt_cancel"));
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Cancelled));
        });
    }

    /// <summary>
    /// A delivery that correctly changes nothing leaves no other evidence anywhere: the payment is untouched, and
    /// <c>ProcessedStripeWebhookEvents</c> records the event id without saying which payment it reached. So
    /// "Stripe says it sent that, we have no record of it" is only answerable from these rows.
    /// </summary>
    [TestCase(PaymentStatus.Success)]
    [TestCase(PaymentStatus.Refunded)]
    public void AStripeSuccessThatChangesNothing_IsStillRecorded(PaymentStatus status)
    {
        var payment = A(status, intentId: "pi_1");

        var changed = payment.RecordStripeSuccess("succeeded", Now, "evt_late");

        var row = Only(payment);
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(payment.Status, Is.EqualTo(status));
            Assert.That(row.Kind, Is.EqualTo(PaymentEventKind.Webhook));
            Assert.That(row.StripeEventId, Is.EqualTo("evt_late"));
            Assert.That(row.FromStatus, Is.EqualTo(status));
            Assert.That(row.ToStatus, Is.EqualTo(status));
            Assert.That(row.Detail, Does.Contain("was not changed"));
        });
    }

    [Test]
    public void ADeclineArrivingAfterTheSuccess_IsStillRecorded()
    {
        var payment = A(PaymentStatus.Success, intentId: "pi_1");

        var changed = payment.RecordDeclinedAttempt("declined", "requires_payment_method", Now, "evt_late_decline");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(Only(payment).Detail, Does.Contain("was not changed"));
            Assert.That(payment.ErrorMessage, Is.Null, "a late decline must not annotate a settled payment");
        });
    }

    [Test]
    public void ARepeatedCancellation_IsStillRecorded()
    {
        var payment = A(PaymentStatus.Cancelled, intentId: "pi_1");

        var changed = payment.RecordStripeCancellation(requestedByEShop: true, "canceled", Now, "evt_again");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(Only(payment).Detail, Does.Contain("was not changed"));
        });
    }

    /// <summary>
    /// It reports what Stripe says about the intent, not what happened to the payment, and every caller follows it in
    /// the same save with a real transition whose row is the one that says what happened.
    /// </summary>
    [Test]
    public void ObservingStripesIntentStatus_WritesNoRow()
    {
        var payment = A(PaymentStatus.Processing, intentId: "pi_1");

        payment.ObserveStripeStatus("processing", Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.Events, Is.Empty);
            Assert.That(payment.StripeStatus, Is.EqualTo("processing"));
        });
    }

    // ---- bounds ----------------------------------------------------------------------------------------------

    [Test]
    public void ADetailLongerThanItsColumn_IsTruncated_NotRejected()
    {
        var payment = A(PaymentStatus.Processing, PaymentMethodType.Mock);

        payment.RecordSimulatedFailure(new string('x', 900), Now);

        Assert.That(Only(payment).Detail, Has.Length.EqualTo(PaymentEvent.MaxDetailLength));
    }

    [Test]
    public void AWebhookRowWithNoEventId_StoresNull_NotAnEmptyString()
    {
        var payment = A(PaymentStatus.Processing, intentId: "pi_1");

        payment.RecordStripeSuccess("succeeded", Now, "   ");

        Assert.That(Only(payment).StripeEventId, Is.Null);
    }
}
