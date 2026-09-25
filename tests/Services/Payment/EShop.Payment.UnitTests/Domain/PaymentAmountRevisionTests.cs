using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.UnitTests.Domain;

/// <summary>
/// frontend-contracts F-47: <see cref="PaymentTransaction.ReviseAmount"/> against every status and method. A payment may
/// follow its order's new total only while nothing has been captured at the old one.
/// </summary>
[TestFixture]
public class PaymentAmountRevisionTests
{
    private static readonly DateTime Earlier = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ChangedAt = new(2026, 9, 25, 10, 34, 15, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 9, 25, 10, 34, 20, DateTimeKind.Utc);

    private static PaymentTransaction A(PaymentStatus status, PaymentMethodType method = PaymentMethodType.Stripe, string intentId = "")
        => new()
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 591.99m,
            Currency = "USD",
            PaymentMethod = method,
            PaymentIntentId = intentId,
            Status = status,
            CreatedAt = Earlier,
            UpdatedAt = Earlier
        };

    [Test]
    public void APendingPayment_TakesTheNewTotal_AndRecordsWhenItBecameTrue()
    {
        var payment = A(PaymentStatus.Pending);

        payment.ReviseAmount(675.99m, ChangedAt, Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.Amount, Is.EqualTo(675.99m));
            Assert.That(payment.AmountAsOf, Is.EqualTo(ChangedAt));
            Assert.That(payment.UpdatedAt, Is.EqualTo(Now));
            Assert.That(payment.Status, Is.EqualTo(PaymentStatus.Pending), "a revision is not a transition");
        });
    }

    [Test]
    public void AProcessingStripePaymentWithAnIntent_TakesTheNewTotal()
    {
        var payment = A(PaymentStatus.Processing, intentId: "pi_1");

        payment.ReviseAmount(675.99m, ChangedAt, Now);

        Assert.That(payment.Amount, Is.EqualTo(675.99m));
    }

    [Test]
    public void ARevision_WritesOneTimelineRow_NamingBothAmounts()
    {
        var payment = A(PaymentStatus.Pending);

        payment.ReviseAmount(675.99m, ChangedAt, Now);

        var row = payment.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.FromStatus, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(row.ToStatus, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(row.Detail, Does.Contain("591.99").And.Contain("675.99"));
        });
    }

    [Test]
    public void TheSameAmount_MovesOnlyAmountAsOf_AndWritesNoRow()
    {
        var payment = A(PaymentStatus.Pending);

        payment.ReviseAmount(591.99m, ChangedAt, Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.AmountAsOf, Is.EqualTo(ChangedAt));
            Assert.That(payment.Events, Is.Empty);
        });
    }

    [Test]
    public void ARevisionNoNewerThanTheLast_IsRefused_SoAnOldTotalCannotComeBack()
    {
        var payment = A(PaymentStatus.Pending);
        payment.ReviseAmount(700m, ChangedAt, Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.AlreadyReflectsTotalAsOf(ChangedAt.AddSeconds(-1)), Is.True);
            Assert.That(payment.AlreadyReflectsTotalAsOf(ChangedAt), Is.True, "a redelivery of the same change");
            Assert.That(payment.AlreadyReflectsTotalAsOf(ChangedAt.AddTicks(1)), Is.False);
        });
        Assert.Throws<DomainException>(() => payment.ReviseAmount(600m, ChangedAt.AddSeconds(-1), Now));
        Assert.That(payment.Amount, Is.EqualTo(700m));
    }

    [Test]
    public void APaymentThatNeverRevised_ReflectsNoInstantYet()
        => Assert.That(A(PaymentStatus.Pending).AlreadyReflectsTotalAsOf(DateTime.MinValue.AddTicks(1)), Is.False);

    [Test]
    public void ANegativeAmount_IsRefused()
        => Assert.Throws<DomainException>(() => A(PaymentStatus.Pending).ReviseAmount(-1m, ChangedAt, Now));

    /// <summary>Captured, closed, or being charged elsewhere: the old amount stands, and that needs a person.</summary>
    [TestCase(PaymentStatus.Success, PaymentMethodType.Stripe, "pi_1")]
    [TestCase(PaymentStatus.Success, PaymentMethodType.Mock, "offline:ref")]
    [TestCase(PaymentStatus.Failed, PaymentMethodType.Stripe, "pi_1")]
    [TestCase(PaymentStatus.Refunded, PaymentMethodType.Stripe, "pi_1")]
    [TestCase(PaymentStatus.Cancelled, PaymentMethodType.Stripe, "")]
    [TestCase(PaymentStatus.Processing, PaymentMethodType.Mock, "")]
    [TestCase(PaymentStatus.Processing, PaymentMethodType.Stripe, "")]
    public void AnyOtherState_IsRefused_AndChangesNothing(PaymentStatus status, PaymentMethodType method, string intentId)
    {
        var payment = A(status, method, intentId);

        Assert.Throws<DomainException>(() => payment.ReviseAmount(675.99m, ChangedAt, Now));

        Assert.Multiple(() =>
        {
            Assert.That(payment.Amount, Is.EqualTo(591.99m));
            Assert.That(payment.AmountAsOf, Is.Null);
            Assert.That(payment.Events, Is.Empty);
        });
    }
}
