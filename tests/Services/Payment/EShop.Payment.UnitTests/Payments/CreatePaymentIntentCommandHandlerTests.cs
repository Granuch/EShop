using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Payment audit Stage 2 (C2, D4): /create-intent pays the payment Payment recorded for the order, for exactly its
/// amount and currency; the request names only the order.
/// </summary>
[TestFixture]
public class CreatePaymentIntentCommandHandlerTests
{
    private PaymentDbContext _db = null!;
    private Mock<IStripeCustomerService> _customers = null!;
    private Mock<IStripePaymentService> _stripe = null!;
    private Mock<IIntegrationEventOutbox> _outbox = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _customers = new Mock<IStripeCustomerService>(MockBehavior.Strict);
        _stripe = new Mock<IStripePaymentService>(MockBehavior.Strict);
        _outbox = new Mock<IIntegrationEventOutbox>();
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private CreatePaymentIntentCommandHandler Handler() => new(
        new PaymentRepository(_db),
        _customers.Object,
        _stripe.Object,
        _outbox.Object,
        _db,
        Mock.Of<ILogger<CreatePaymentIntentCommandHandler>>());

    private async Task<PaymentTransaction> SeedAsync(
        string userId = "user-1",
        decimal amount = 100m,
        PaymentStatus status = PaymentStatus.Pending,
        string method = "Stripe",
        string intentId = "")
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = userId,
            Amount = amount,
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

    private Task<PaymentTransaction> StoredAsync(Guid orderId)
        => _db.PaymentTransactions.AsNoTracking().SingleAsync(x => x.OrderId == orderId);

    private void StripeAccepts(Action<StripePaymentIntentRequest>? capture = null)
    {
        _customers
            .Setup(x => x.CreateOrGetCustomerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cus_test_123");
        _stripe
            .Setup(x => x.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StripePaymentIntentRequest, CancellationToken>((r, _) => capture?.Invoke(r))
            .ReturnsAsync(new StripePaymentIntentResult("pi_test_123", "cs_test_123", "requires_payment_method"));
    }

    [Test]
    public async Task Handle_ChargesTheRecordedAmountAndCurrency_AndStartsThePayment()
    {
        var seeded = await SeedAsync(amount: 123.45m);
        StripePaymentIntentRequest? sent = null;
        StripeAccepts(r => sent = r);

        var result = await Handler().Handle(
            new CreatePaymentIntentCommand(seeded.OrderId, "user-1", false, "user@test.com"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.ClientSecret, Is.EqualTo("cs_test_123"));
            Assert.That(sent!.Amount, Is.EqualTo(123.45m));
            Assert.That(sent.Currency, Is.EqualTo("USD"));
            Assert.That(sent.PaymentId, Is.EqualTo(seeded.Id));
        });

        var stored = await StoredAsync(seeded.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(stored.PaymentIntentId, Is.EqualTo("pi_test_123"));
            Assert.That(stored.StripeCustomerId, Is.EqualTo("cus_test_123"));
        });
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Once);
    }

    /// <summary>OrderCreatedEvent has not reached Payment yet. Nothing may be created from the request instead.</summary>
    [Test]
    public async Task Handle_BeforeTheOrdersPaymentIsRecorded_IsNotReady_AndStripeIsNotCalled()
    {
        var orderId = Guid.NewGuid();

        var result = await Handler().Handle(
            new CreatePaymentIntentCommand(orderId, "user-1", false, null), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_NOT_READY"));
        Assert.That(await _db.PaymentTransactions.AnyAsync(), Is.False);
        _stripe.VerifyNoOtherCalls();
        _customers.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Handle_ForAnotherUsersOrder_IsNotFound_AndStripeIsNotCalled()
    {
        var seeded = await SeedAsync(userId: "user-2");

        var result = await Handler().Handle(
            new CreatePaymentIntentCommand(seeded.OrderId, "user-1", false, null), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_NOT_FOUND"));
        Assert.That((await StoredAsync(seeded.OrderId)).Status, Is.EqualTo(PaymentStatus.Pending));
        _stripe.VerifyNoOtherCalls();
        _customers.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Handle_AnAdmin_StartsTheCustomersPayment_UnderTheCustomer()
    {
        var seeded = await SeedAsync(userId: "user-2");
        StripeAccepts();

        var result = await Handler().Handle(
            new CreatePaymentIntentCommand(seeded.OrderId, "admin-1", true, null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _customers.Verify(x => x.CreateOrGetCustomerAsync("user-2", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(PaymentStatus.Processing, "Stripe", "pi_existing")]
    [TestCase(PaymentStatus.Pending, "Stripe", "pi_existing")]
    [TestCase(PaymentStatus.Success, "Stripe", "pi_existing")]
    [TestCase(PaymentStatus.Cancelled, "None", "")]
    [TestCase(PaymentStatus.Pending, "Mock", "")]
    // A Stripe payment that never got an intent and has ended: the order was cancelled before the customer began
    // paying, or an earlier attempt failed. Only the status says it is over (the S2 F2 round found this uncovered).
    [TestCase(PaymentStatus.Cancelled, "Stripe", "")]
    [TestCase(PaymentStatus.Failed, "Stripe", "")]
    public async Task Handle_WhenThePaymentIsNotAwaitingStripe_IsAConflict(PaymentStatus status, string method, string intentId)
    {
        var seeded = await SeedAsync(status: status, method: method, intentId: intentId);

        var result = await Handler().Handle(
            new CreatePaymentIntentCommand(seeded.OrderId, "user-1", false, null), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_ALREADY_EXISTS"));
        _stripe.VerifyNoOtherCalls();
        _customers.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Handle_WhenCustomerCreationFails_ShouldMarkPaymentFailedAndEnqueueFailedEvent()
    {
        var seeded = await SeedAsync();
        _customers
            .Setup(x => x.CreateOrGetCustomerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stripe customer API error"));

        var result = await Handler().Handle(
            new CreatePaymentIntentCommand(seeded.OrderId, "user-1", false, "user@test.com"), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("STRIPE_PAYMENT_INTENT_FAILED"));
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Once);
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Never);

        var stored = await StoredAsync(seeded.OrderId);
        Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Failed));
        Assert.That(stored.ErrorMessage, Is.EqualTo("Failed to create Stripe payment intent."));
    }

    [Test]
    public async Task Handle_WhenIntentCreationFails_ShouldMarkPaymentFailedAndEnqueueFailedEvent()
    {
        var seeded = await SeedAsync();
        _customers
            .Setup(x => x.CreateOrGetCustomerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cus_test_123");
        _stripe
            .Setup(x => x.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stripe payment intent API error"));

        var result = await Handler().Handle(
            new CreatePaymentIntentCommand(seeded.OrderId, "user-1", false, "user@test.com"), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("STRIPE_PAYMENT_INTENT_FAILED"));
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Once);
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Never);

        var stored = await StoredAsync(seeded.OrderId);
        Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Failed));
        Assert.That(stored.ErrorMessage, Is.EqualTo("Failed to create Stripe payment intent."));
    }
}
