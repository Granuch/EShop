using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.UnitTests.Domain;

/// <summary>
/// Admin panel S10 (endpoint #58, decision Q6a): <see cref="PaymentTransaction.SettleOffline"/>, the transition an
/// operator makes when the money arrived by bank transfer or in cash.
/// </summary>
[TestFixture]
public class PaymentOfflineSettlementTests
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
            ErrorMessage = "earlier note",
            CreatedAt = Earlier,
            UpdatedAt = Earlier
        };

    private static void AssertRefused(PaymentTransaction payment, Action transition)
    {
        var before = (payment.Status, payment.PaymentMethod, payment.PaymentIntentId, payment.ErrorMessage,
            payment.ProcessedAt, payment.UpdatedAt);

        Assert.Throws<DomainException>(() => transition());

        Assert.That(
            (payment.Status, payment.PaymentMethod, payment.PaymentIntentId, payment.ErrorMessage,
                payment.ProcessedAt, payment.UpdatedAt),
            Is.EqualTo(before),
            "a refused transition changes nothing");
    }

    [TestCase(PaymentMethodType.Stripe)]
    [TestCase(PaymentMethodType.Mock)]
    public void APendingPayment_IsSettled_AsAMockSuccess(PaymentMethodType method)
    {
        var payment = A(PaymentStatus.Pending, method);

        payment.SettleOffline("TRF-2026-0042", Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Success));
            Assert.That(payment.PaymentMethod, Is.EqualTo(PaymentMethodType.Mock));
            Assert.That(payment.ProcessedAt, Is.EqualTo(Now));
            Assert.That(payment.UpdatedAt, Is.EqualTo(Now));
            Assert.That(payment.Amount, Is.EqualTo(40m), "the recorded amount is never touched by a settlement");
            Assert.That(payment.Currency, Is.EqualTo("USD"));
        });
    }

    /// <summary>
    /// The prefix is what stops an operator's typed reference being mistaken for — or colliding with — a Stripe intent
    /// id, here and on the order that receives it through <c>PaymentSuccessEvent</c>.
    /// </summary>
    [Test]
    public void TheReference_IsStoredUnderTheOfflinePrefix_AndTrimmed()
    {
        var payment = A(PaymentStatus.Pending);

        payment.SettleOffline("  TRF-9  ", Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.PaymentIntentId, Is.EqualTo("offline:TRF-9"));
            Assert.That(payment.PaymentIntentId, Does.StartWith(PaymentTransaction.OfflineReferencePrefix));
            Assert.That(PaymentTransaction.OfflineReference(" TRF-9 "), Is.EqualTo(payment.PaymentIntentId),
                "a caller's uniqueness pre-check must build the same string the transition stores");
        });
    }

    /// <summary>A settlement clears whatever an earlier attempt left, as every other success does.</summary>
    [Test]
    public void ASettlement_ClearsAnEarlierNote()
    {
        var payment = A(PaymentStatus.Pending);

        payment.SettleOffline("TRF-1", Now);

        Assert.That(payment.ErrorMessage, Is.Null);
    }

    [TestCase(PaymentStatus.Processing)]
    [TestCase(PaymentStatus.Success)]
    [TestCase(PaymentStatus.Failed)]
    [TestCase(PaymentStatus.Refunded)]
    [TestCase(PaymentStatus.Cancelled)]
    public void AnyOtherStatus_IsRefused(PaymentStatus status)
    {
        var payment = A(status);

        AssertRefused(payment, () => payment.SettleOffline("TRF-1", Now));
    }

    /// <summary>
    /// The invariant the Pending-only rule rests on, stated rather than trusted: a payment with an intent has an
    /// attempt outstanding at a provider, and settling it offline would strand an intent the customer can still pay.
    /// </summary>
    [Test]
    public void APendingPaymentThatSomehowHasAnIntent_IsRefused()
    {
        var payment = A(PaymentStatus.Pending, PaymentMethodType.Stripe, "pi_live");

        AssertRefused(payment, () => payment.SettleOffline("TRF-1", Now));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ABlankReference_IsRefused(string reference)
    {
        var payment = A(PaymentStatus.Pending);

        AssertRefused(payment, () => payment.SettleOffline(reference, Now));
    }

    /// <summary>
    /// <c>EnqueuePaymentSucceeded</c> refuses a payment whose status is not Success and reads <c>ProcessedAt</c> for
    /// the event time, so a settlement that set neither would fail at the publisher rather than here.
    /// </summary>
    [Test]
    public void ASettledPayment_CanAnnounceItself_AsASuccess()
    {
        var payment = A(PaymentStatus.Pending);

        payment.SettleOffline("TRF-1", Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Success));
            Assert.That(payment.ProcessedAt, Is.Not.Null);
        });
    }
}
