using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Commands.SettleOfflinePayment;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Admin panel S10 (endpoint #58, decision Q6a). Everything the aggregate cannot decide for itself: which payment is
/// settled, which of two things wrong with a request is reported, and which events leave.
/// </summary>
[TestFixture]
public class SettleOfflinePaymentCommandHandlerTests
{
    private PaymentDbContext _db = null!;
    private Mock<IIntegrationEventOutbox> _outbox = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _outbox = new Mock<IIntegrationEventOutbox>();
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private SettleOfflinePaymentCommandHandler Handler()
        => new(new PaymentRepository(_db), _outbox.Object, _db);

    private async Task<PaymentTransaction> SeedAsync(
        PaymentStatus status = PaymentStatus.Pending,
        decimal amount = 100m,
        string intentId = "")
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "customer-1",
            Amount = amount,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
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

    private Task<PaymentTransaction> StoredAsync(Guid orderId)
        => _db.PaymentTransactions.AsNoTracking().SingleAsync(x => x.OrderId == orderId);

    [Test]
    public async Task Handle_SettlesTheRecordedPayment_ForTheRecordedAmount()
    {
        var seeded = await SeedAsync(amount: 123.45m);

        var result = await Handler().Handle(
            new SettleOfflinePaymentCommand(seeded.OrderId, "TRF-7"), CancellationToken.None);

        var stored = await StoredAsync(seeded.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Success));
            Assert.That(stored.PaymentMethod, Is.EqualTo(PaymentMethodType.Mock));
            Assert.That(stored.PaymentIntentId, Is.EqualTo("offline:TRF-7"));
            Assert.That(stored.Amount, Is.EqualTo(123.45m));
        });
    }

    /// <summary>
    /// <c>PaymentSuccessEvent</c> carries the settlement to Ordering, and <c>PaymentCompletedEvent</c> to
    /// Notification. <c>PaymentCreatedEvent</c> must not be sent: it means "an attempt has started and nothing is
    /// charged yet", which is false of money that already arrived, and Notification's template for it says
    /// "Payment started".
    /// </summary>
    [Test]
    public async Task Handle_AnnouncesTheSuccess_AndNotAStartedAttempt()
    {
        var seeded = await SeedAsync(amount: 60m);

        await Handler().Handle(new SettleOfflinePaymentCommand(seeded.OrderId, "TRF-7"), CancellationToken.None);

        _outbox.Verify(x => x.Enqueue(
            It.Is<PaymentSuccessEvent>(e =>
                e.OrderId == seeded.OrderId && e.Amount == 60m && e.Currency == "USD"
                && e.PaymentIntentId == "offline:TRF-7"),
            It.IsAny<string?>()), Times.Once);
        _outbox.Verify(x => x.Enqueue(
            It.Is<PaymentCompletedEvent>(e => e.OrderId == seeded.OrderId), It.IsAny<string?>()), Times.Once);
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithNoRecordedPayment_IsNotFound_AndCreatesNothing()
    {
        var orderId = Guid.NewGuid();

        var result = await Handler().Handle(
            new SettleOfflinePaymentCommand(orderId, "TRF-7"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_NOT_FOUND"));
        });
        Assert.That(await _db.PaymentTransactions.CountAsync(), Is.Zero,
            "nothing may be created from the request");
        _outbox.VerifyNoOtherCalls();
    }

    [TestCase(PaymentStatus.Processing)]
    [TestCase(PaymentStatus.Success)]
    [TestCase(PaymentStatus.Failed)]
    [TestCase(PaymentStatus.Refunded)]
    [TestCase(PaymentStatus.Cancelled)]
    public async Task Handle_WithAPaymentThatIsNotPending_IsRefused_AndChangesNothing(PaymentStatus status)
    {
        var seeded = await SeedAsync(status);

        var result = await Handler().Handle(
            new SettleOfflinePaymentCommand(seeded.OrderId, "TRF-7"), CancellationToken.None);

        var stored = await StoredAsync(seeded.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_NOT_PENDING"));
            Assert.That(stored.Status, Is.EqualTo(status));
            Assert.That(stored.PaymentIntentId, Is.Empty);
        });
        _outbox.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The reference is the operator's evidence, so the same one cannot stand for two orders. The pre-check names the
    /// order that already holds it; the filtered unique index behind it only says "duplicate, retry", which here can
    /// never succeed.
    /// </summary>
    [Test]
    public async Task Handle_WithAReferenceAlreadyUsed_NamesTheOrderThatHoldsIt_AndChangesNothing()
    {
        var first = await SeedAsync();
        await Handler().Handle(new SettleOfflinePaymentCommand(first.OrderId, "TRF-7"), CancellationToken.None);
        _db.ChangeTracker.Clear();

        var second = await SeedAsync();
        var result = await Handler().Handle(
            new SettleOfflinePaymentCommand(second.OrderId, "TRF-7"), CancellationToken.None);

        var stored = await StoredAsync(second.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_REFERENCE_IN_USE"));
            Assert.That(result.Error!.Message, Does.Contain(first.OrderId.ToString()));
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Pending),
                "the second payment must be untouched — TransactionBehavior commits on a Result failure too");
            Assert.That(stored.PaymentIntentId, Is.Empty);
        });
    }

    /// <summary>
    /// The prefix is what makes the reference space disjoint from Stripe's. Without it an operator who typed a real
    /// intent id would take the row the webhook is going to look for.
    /// </summary>
    [Test]
    public async Task Handle_WithAReferenceThatLooksLikeAStripeIntent_DoesNotCollideWithOne()
    {
        var stripePayment = await SeedAsync(PaymentStatus.Processing, intentId: "pi_live_1");
        var pending = await SeedAsync();

        var result = await Handler().Handle(
            new SettleOfflinePaymentCommand(pending.OrderId, "pi_live_1"), CancellationToken.None);

        var stored = await StoredAsync(pending.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(stored.PaymentIntentId, Is.EqualTo("offline:pi_live_1"));
        });
        Assert.That((await StoredAsync(stripePayment.OrderId)).PaymentIntentId, Is.EqualTo("pi_live_1"));
    }
}
