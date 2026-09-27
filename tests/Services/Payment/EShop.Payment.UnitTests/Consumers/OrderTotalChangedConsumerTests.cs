using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Payment.UnitTests.Consumers;

/// <summary>
/// frontend-contracts F-47: a payment charges its order's current total. Before this consumer, an item change on a
/// Pending order never reached Payment, the customer was charged the old total, and Ordering refused the success,
/// leaving a paid order Pending for good.
/// </summary>
[TestFixture]
public class OrderTotalChangedConsumerTests
{
    private static readonly DateTime ChangedAt = new(2026, 9, 25, 10, 34, 15, DateTimeKind.Utc);

    private PaymentDbContext _db = null!;
    private Mock<IStripePaymentService> _stripe = null!;
    private OrderTotalChangedConsumer _consumer = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"order-total-changed-{Guid.NewGuid():N}")
            .Options);
        // Strict: a Stripe call nobody set up fails the test, which is how "no call" is asserted.
        _stripe = new Mock<IStripePaymentService>(MockBehavior.Strict);
        _consumer = new OrderTotalChangedConsumer(
            _db,
            new PaymentRepository(_db),
            _db,
            _stripe.Object,
            Mock.Of<ILogger<OrderTotalChangedConsumer>>());
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private async Task<PaymentTransaction> SeedAsync(
        PaymentStatus status,
        PaymentMethodType method = PaymentMethodType.Stripe,
        string intentId = "",
        DateTime? amountAsOf = null)
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 591.99m,
            AmountAsOf = amountAsOf,
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

    private static ConsumeContext<OrderTotalChangedEvent> Changed(
        Guid orderId, decimal newTotal, DateTime? asOf = null, string currency = "USD")
    {
        var context = new Mock<ConsumeContext<OrderTotalChangedEvent>>();
        context.SetupGet(x => x.Message).Returns(new OrderTotalChangedEvent
        {
            OrderId = orderId,
            UserId = "user-1",
            NewTotal = newTotal,
            Currency = currency,
            TotalAsOf = asOf ?? ChangedAt
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

    [Test]
    public async Task APendingPayment_TakesTheNewTotal_WithoutCallingStripe()
    {
        var payment = await SeedAsync(PaymentStatus.Pending);

        await _consumer.Consume(Changed(payment.OrderId, 675.99m));

        var stored = await StoredAsync(payment.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Amount, Is.EqualTo(675.99m));
            Assert.That(stored.AmountAsOf, Is.EqualTo(ChangedAt));
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(StoredClaims, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AStripeIntentAlreadyCreated_IsChangedAtStripe_ThenRecorded()
    {
        var payment = await SeedAsync(PaymentStatus.Processing, intentId: "pi_live_1");
        _stripe.Setup(s => s.UpdatePaymentIntentAmountAsync("pi_live_1", 675.99m, "USD", It.IsAny<CancellationToken>()))
            .ReturnsAsync("requires_payment_method");

        await _consumer.Consume(Changed(payment.OrderId, 675.99m));

        _stripe.Verify(s => s.UpdatePaymentIntentAmountAsync("pi_live_1", 675.99m, "USD", It.IsAny<CancellationToken>()), Times.Once);
        var stored = await StoredAsync(payment.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Amount, Is.EqualTo(675.99m));
            Assert.That(stored.StripeStatus, Is.EqualTo("requires_payment_method"));
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Processing));
        });
    }

    [Test]
    public async Task StripeRefusingTheChange_DeadLettersAtOnce_AndLeavesTheOldAmount()
    {
        var payment = await SeedAsync(PaymentStatus.Processing, intentId: "pi_live_1");
        _stripe.Setup(s => s.UpdatePaymentIntentAmountAsync("pi_live_1", 675.99m, "USD", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PaymentIntentNotUpdatableException("pi_live_1", "status of succeeded"));

        var ex = Assert.ThrowsAsync<PaymentAmountRevisionFailedException>(
            () => _consumer.Consume(Changed(payment.OrderId, 675.99m)));

        Assert.That(ex, Is.InstanceOf<ArgumentException>(), "the retry policies ignore ArgumentException, so it goes straight to the error queue");
        Assert.That((await StoredAsync(payment.OrderId)).Amount, Is.EqualTo(591.99m));
    }

    [TestCase(PaymentStatus.Success, PaymentMethodType.Stripe, "pi_live_1")]
    [TestCase(PaymentStatus.Success, PaymentMethodType.Mock, "offline:ref-1")]
    [TestCase(PaymentStatus.Processing, PaymentMethodType.Mock, "")]
    public async Task AnAmountAlreadyCharged_DeadLetters_WithoutCallingStripe(
        PaymentStatus status, PaymentMethodType method, string intentId)
    {
        var payment = await SeedAsync(status, method, intentId);

        Assert.ThrowsAsync<PaymentAmountRevisionFailedException>(
            () => _consumer.Consume(Changed(payment.OrderId, 675.99m)));

        Assert.That((await StoredAsync(payment.OrderId)).Amount, Is.EqualTo(591.99m));
    }

    [TestCase(PaymentStatus.Failed)]
    [TestCase(PaymentStatus.Cancelled)]
    [TestCase(PaymentStatus.Refunded)]
    public async Task AClosedPayment_IsLeftAlone(PaymentStatus status)
    {
        var payment = await SeedAsync(status, intentId: "pi_live_1");

        await _consumer.Consume(Changed(payment.OrderId, 675.99m));

        var stored = await StoredAsync(payment.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Amount, Is.EqualTo(591.99m));
            Assert.That(stored.Status, Is.EqualTo(status));
            Assert.That(StoredClaims, Is.EqualTo(1), "handled, so not redelivered");
        });
    }

    [Test]
    public void AChangeBeforeThePaymentIsRecorded_IsRetried_NotDeadLettered()
    {
        var ex = Assert.ThrowsAsync<PaymentNotRecordedYetException>(
            () => _consumer.Consume(Changed(Guid.NewGuid(), 675.99m)));

        Assert.That(ex, Is.Not.InstanceOf<ArgumentException>(), "the retry policies must retry it");
    }

    [Test]
    public async Task AnOlderChangeDeliveredLate_CannotPutTheOldTotalBack()
    {
        var payment = await SeedAsync(PaymentStatus.Pending);

        await _consumer.Consume(Changed(payment.OrderId, 700.00m, ChangedAt));
        await _consumer.Consume(Changed(payment.OrderId, 650.00m, ChangedAt.AddSeconds(-5)));

        var stored = await StoredAsync(payment.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Amount, Is.EqualTo(700.00m));
            Assert.That(stored.AmountAsOf, Is.EqualTo(ChangedAt));
        });
    }

    [Test]
    public async Task AnOlderChange_NeverReachesStripe()
    {
        var payment = await SeedAsync(PaymentStatus.Processing, intentId: "pi_live_1", amountAsOf: ChangedAt);

        await _consumer.Consume(Changed(payment.OrderId, 650.00m, ChangedAt.AddSeconds(-5)));

        Assert.That((await StoredAsync(payment.OrderId)).Amount, Is.EqualTo(591.99m));
    }

    [Test]
    public async Task TheSameAmount_DoesNotCallStripe_ButRecordsTheInstant()
    {
        var payment = await SeedAsync(PaymentStatus.Processing, intentId: "pi_live_1");

        await _consumer.Consume(Changed(payment.OrderId, 591.99m));

        Assert.That((await StoredAsync(payment.OrderId)).AmountAsOf, Is.EqualTo(ChangedAt));
    }

    [Test]
    public async Task ATotalInAnotherCurrency_DeadLetters()
    {
        var payment = await SeedAsync(PaymentStatus.Pending);

        Assert.ThrowsAsync<PaymentAmountRevisionFailedException>(
            () => _consumer.Consume(Changed(payment.OrderId, 675.99m, currency: "EUR")));

        Assert.That((await StoredAsync(payment.OrderId)).Amount, Is.EqualTo(591.99m));
    }
}
