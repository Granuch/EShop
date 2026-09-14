using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.UnitTests.Domain;

/// <summary>
/// Payment audit Stage 7 (H5): every transition of <see cref="PaymentTransaction"/> against every status (and every payment
/// method where it matters). Before this stage there was no entity to test. Six writers each checked the status with their
/// own guard list, and C1 and H1 were both a writer making a change no other writer would have made.
/// </summary>
[TestFixture]
public class PaymentTransactionTests
{
    private static readonly DateTime Earlier = new(2026, 9, 13, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    private static IEnumerable<PaymentStatus> AllStatuses => Enum.GetValues<PaymentStatus>();

    private static IEnumerable<PaymentMethodType> AllMethods => Enum.GetValues<PaymentMethodType>();

    private static PaymentTransaction A(PaymentStatus status, PaymentMethodType method = PaymentMethodType.Stripe, string intentId = "")
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
            ErrorMessage = "earlier note",
            CreatedAt = Earlier,
            UpdatedAt = Earlier
        };

    private static void AssertRefused(PaymentTransaction payment, Action transition)
    {
        var before = (payment.Status, payment.PaymentMethod, payment.PaymentIntentId, payment.ErrorMessage, payment.UpdatedAt);

        Assert.Throws<DomainException>(() => transition());

        Assert.That((payment.Status, payment.PaymentMethod, payment.PaymentIntentId, payment.ErrorMessage, payment.UpdatedAt),
            Is.EqualTo(before), "a refused transition changes nothing");
    }

    // ---- Creation ----

    [TestCase(PaymentMethodType.Mock)]
    [TestCase(PaymentMethodType.Stripe)]
    public void ANewOrdersPayment_IsPending_InUsd_WithNoIntent(PaymentMethodType method)
    {
        var orderId = Guid.NewGuid();

        var payment = PaymentTransaction.RecordForOrder(orderId, "user-1", 25m, method, Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.OrderId, Is.EqualTo(orderId));
            Assert.That(payment.Amount, Is.EqualTo(25m));
            Assert.That(payment.Currency, Is.EqualTo("USD"));
            Assert.That(payment.PaymentMethod, Is.EqualTo(method));
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(payment.PaymentIntentId, Is.Empty);
            Assert.That(payment.CreatedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void ANewOrdersPayment_HasAMethod() =>
        Assert.Throws<DomainException>(() => PaymentTransaction.RecordForOrder(Guid.NewGuid(), "user-1", 25m, PaymentMethodType.None, Now));

    [Test]
    public void ANewOrdersPayment_IsNeverForANegativeAmount() =>
        Assert.Throws<DomainException>(() => PaymentTransaction.RecordForOrder(Guid.NewGuid(), "user-1", -1m, PaymentMethodType.Mock, Now));

    [Test]
    public void AnOrderCancelledBeforeItsPayment_LeavesAFinalRecord_ThatChargesNothing()
    {
        var payment = PaymentTransaction.RecordCancelledBeforeCreation(Guid.NewGuid(), "user-1", "Order cancelled: changed my mind", Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Cancelled));
            Assert.That(payment.PaymentMethod, Is.EqualTo(PaymentMethodType.None));
            Assert.That(payment.Amount, Is.Zero);
            Assert.That(payment.ErrorMessage, Is.EqualTo("Order cancelled: changed my mind"));
            Assert.That(payment.ProcessedAt, Is.EqualTo(Now));
        });
    }

    // ---- The simulator ----

    [Test]
    public void StartSimulated_TakesAPendingPayment_OrResumesASimulatedOne(
        [ValueSource(nameof(AllStatuses))] PaymentStatus status,
        [ValueSource(nameof(AllMethods))] PaymentMethodType method)
    {
        var payment = A(status, method);

        if (status == PaymentStatus.Pending || (status == PaymentStatus.Processing && method == PaymentMethodType.Mock))
        {
            payment.StartSimulated(Now);
            Assert.That((payment.Status, payment.PaymentMethod), Is.EqualTo((PaymentStatus.Processing, PaymentMethodType.Mock)));
        }
        else
        {
            AssertRefused(payment, () => payment.StartSimulated(Now));
        }
    }

    [Test]
    public void StartSimulated_NeverTakesAPaymentThatHasAnIntent() =>
        AssertRefused(A(PaymentStatus.Pending, PaymentMethodType.Stripe, "pi_1"), () => A(PaymentStatus.Pending, PaymentMethodType.Stripe, "pi_1").StartSimulated(Now));

    [Test]
    public void TheSimulatorsAnswer_OnlySettlesASimulatedPaymentInFlight(
        [ValueSource(nameof(AllStatuses))] PaymentStatus status,
        [ValueSource(nameof(AllMethods))] PaymentMethodType method)
    {
        var succeeding = A(status, method);
        var failing = A(status, method);

        if (status == PaymentStatus.Processing && method == PaymentMethodType.Mock)
        {
            succeeding.RecordSimulatedSuccess("pi_mock_1", Now);
            failing.RecordSimulatedFailure("Card declined", Now);

            Assert.Multiple(() =>
            {
                Assert.That(succeeding.Status, Is.EqualTo(PaymentStatus.Success));
                Assert.That(succeeding.PaymentIntentId, Is.EqualTo("pi_mock_1"));
                Assert.That(succeeding.ErrorMessage, Is.Null);
                Assert.That(succeeding.ProcessedAt, Is.EqualTo(Now));
                Assert.That(failing.Status, Is.EqualTo(PaymentStatus.Failed));
                Assert.That(failing.ErrorMessage, Is.EqualTo("Card declined"));
            });
        }
        else
        {
            AssertRefused(succeeding, () => succeeding.RecordSimulatedSuccess("pi_mock_1", Now));
            AssertRefused(failing, () => failing.RecordSimulatedFailure("Card declined", Now));
        }
    }

    [Test]
    public void ASimulatedSuccess_NeedsThePaymentId()
    {
        var payment = A(PaymentStatus.Processing, PaymentMethodType.Mock);
        AssertRefused(payment, () => payment.RecordSimulatedSuccess(" ", Now));
    }

    // ---- Stripe ----

    [Test]
    public void StartStripePayment_OnlyForAPendingStripePaymentWithNoIntent(
        [ValueSource(nameof(AllStatuses))] PaymentStatus status,
        [ValueSource(nameof(AllMethods))] PaymentMethodType method)
    {
        var payment = A(status, method);

        if (status == PaymentStatus.Pending && method == PaymentMethodType.Stripe)
        {
            payment.StartStripePayment("pi_1", "cus_1", "requires_payment_method", Now);
            Assert.Multiple(() =>
            {
                Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Processing));
                Assert.That(payment.PaymentIntentId, Is.EqualTo("pi_1"));
                Assert.That(payment.StripeCustomerId, Is.EqualTo("cus_1"));
                Assert.That(payment.StripeStatus, Is.EqualTo("requires_payment_method"));
            });
        }
        else
        {
            AssertRefused(payment, () => payment.StartStripePayment("pi_1", "cus_1", "requires_payment_method", Now));
        }
    }

    [Test]
    public void StartStripePayment_OneIntentPerPayment()
    {
        var payment = A(PaymentStatus.Pending, PaymentMethodType.Stripe, "pi_existing");
        AssertRefused(payment, () => payment.StartStripePayment("pi_2", "cus_1", "requires_payment_method", Now));
    }

    [Test]
    public void StartStripePayment_NeedsTheIntent()
    {
        var payment = A(PaymentStatus.Pending);
        AssertRefused(payment, () => payment.StartStripePayment("", "cus_1", "requires_payment_method", Now));
    }

    [Test]
    public void StripesSuccess_IsRecordedFromEveryStateButSuccessAndRefunded([ValueSource(nameof(AllStatuses))] PaymentStatus status)
    {
        var payment = A(status, PaymentMethodType.Stripe, "pi_1");

        var changed = payment.RecordStripeSuccess("succeeded", Now);

        var applies = status is not (PaymentStatus.Success or PaymentStatus.Refunded);
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.EqualTo(applies));
            Assert.That(payment.Status, Is.EqualTo(applies ? PaymentStatus.Success : status), "a refund is never undone");
            Assert.That(payment.ErrorMessage, Is.EqualTo(applies ? null : "earlier note"));
        });
    }

    /// <summary>Stage 5 (H1, D3): a decline records why, and never changes the status.</summary>
    [Test]
    public void ADecline_IsRecordedOnlyOnAPaymentInFlight_AndNeverChangesTheStatus([ValueSource(nameof(AllStatuses))] PaymentStatus status)
    {
        var payment = A(status, PaymentMethodType.Stripe, "pi_1");

        var changed = payment.RecordDeclinedAttempt("Your card was declined.", "requires_payment_method", Now);

        var applies = status is PaymentStatus.Pending or PaymentStatus.Processing;
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.EqualTo(applies));
            Assert.That(payment.Status, Is.EqualTo(status));
            Assert.That(payment.ErrorMessage, Is.EqualTo(applies ? "Your card was declined." : "earlier note"));
        });
    }

    /// <summary>Ordering audit Stage 21 (D17): tagged by us, a cancelled order; untagged, a failed payment.</summary>
    [Test]
    public void StripesCancellation_EndsAPaymentThatIsNotOver(
        [ValueSource(nameof(AllStatuses))] PaymentStatus status,
        [Values] bool requestedByEShop)
    {
        var payment = A(status, PaymentMethodType.Stripe, "pi_1");

        var changed = payment.RecordStripeCancellation(requestedByEShop, "canceled", Now);

        var applies = status is not (PaymentStatus.Success or PaymentStatus.Refunded or PaymentStatus.Cancelled);
        var expected = !applies ? status : requestedByEShop ? PaymentStatus.Cancelled : PaymentStatus.Failed;
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.EqualTo(applies));
            Assert.That(payment.Status, Is.EqualTo(expected));
        });
    }

    // ---- Cancellation and refunds ----

    [Test]
    public void Cancel_OnlyAPaymentInFlight([ValueSource(nameof(AllStatuses))] PaymentStatus status)
    {
        var payment = A(status);

        if (status is PaymentStatus.Pending or PaymentStatus.Processing)
        {
            payment.Cancel("Order cancelled: changed my mind", Now);
            Assert.That((payment.Status, payment.ErrorMessage), Is.EqualTo((PaymentStatus.Cancelled, "Order cancelled: changed my mind")));
        }
        else
        {
            AssertRefused(payment, () => payment.Cancel("Order cancelled: changed my mind", Now));
        }
    }

    [Test]
    public void ObservingStripe_NeverChangesTheStatus([ValueSource(nameof(AllStatuses))] PaymentStatus status)
    {
        var payment = A(status, PaymentMethodType.Stripe, "pi_1");

        payment.ObserveStripeStatus("canceled", Now);

        Assert.That((payment.Status, payment.StripeStatus), Is.EqualTo((status, "canceled")));
    }

    /// <summary>
    /// Refunded once only, and never a payment that was not captured (Cancelled). Allowed from a payment the record still
    /// shows in flight or Failed: the automatic refund acts only on Stripe's word that it captured the money.
    /// </summary>
    [Test]
    public void MarkRefunded_ExceptARefundedOrCancelledPayment([ValueSource(nameof(AllStatuses))] PaymentStatus status)
    {
        var payment = A(status, PaymentMethodType.Stripe, "pi_1");

        if (status is PaymentStatus.Refunded or PaymentStatus.Cancelled)
        {
            AssertRefused(payment, () => payment.MarkRefunded(Now));
        }
        else
        {
            payment.MarkRefunded(Now);
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Refunded));
        }
    }

    [Test]
    public void AnnotateRefund_OnlyARefundedPayment([ValueSource(nameof(AllStatuses))] PaymentStatus status)
    {
        var payment = A(status);

        if (status == PaymentStatus.Refunded)
        {
            payment.AnnotateRefund("Order cancelled: changed my mind");
            Assert.That(payment.ErrorMessage, Is.EqualTo("Order cancelled: changed my mind"));
        }
        else
        {
            AssertRefused(payment, () => payment.AnnotateRefund("Order cancelled: changed my mind"));
        }
    }
}
