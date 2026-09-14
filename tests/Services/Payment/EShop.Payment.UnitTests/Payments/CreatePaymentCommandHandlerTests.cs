using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Commands.CreatePayment;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Payment audit Stage 3 (H4, D2): the admin tool settles the order's recorded Pending payment through the simulator,
/// for the recorded amount. It no longer creates a payment from the request.
/// </summary>
[TestFixture]
public class CreatePaymentCommandHandlerTests
{
    private PaymentDbContext _db = null!;
    private Mock<IPaymentProcessor> _processor = null!;
    private Mock<IIntegrationEventOutbox> _outbox = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _processor = new Mock<IPaymentProcessor>(MockBehavior.Strict);
        _outbox = new Mock<IIntegrationEventOutbox>();
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    private CreatePaymentCommandHandler Handler() => new(new PaymentRepository(_db), _processor.Object, _outbox.Object, _db);

    private async Task<PaymentTransaction> SeedAsync(PaymentStatus status = PaymentStatus.Pending, decimal amount = 100m)
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "customer-1",
            Amount = amount,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
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
        _processor.Setup(x => x.ProcessPaymentAsync(seeded.OrderId, 123.45m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Successful("pi_handler"));

        var result = await Handler().Handle(new CreatePaymentCommand(seeded.OrderId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo("SUCCESS"));
        var stored = await StoredAsync(seeded.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Success));
            Assert.That(stored.PaymentMethod, Is.EqualTo(PaymentMethodType.Mock));
            Assert.That(stored.Amount, Is.EqualTo(123.45m));
            Assert.That(stored.UserId, Is.EqualTo("customer-1"));
        });
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Once);
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentSuccessEvent>(), It.IsAny<string?>()), Times.Once);
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCompletedEvent>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenNoPaymentIsRecordedForTheOrder_IsNotFound_AndCreatesNothing()
    {
        var result = await Handler().Handle(new CreatePaymentCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_NOT_FOUND"));
        Assert.That(await _db.PaymentTransactions.AnyAsync(), Is.False);
        _processor.VerifyNoOtherCalls();
    }

    [TestCase(PaymentStatus.Processing)]
    [TestCase(PaymentStatus.Success)]
    [TestCase(PaymentStatus.Failed)]
    [TestCase(PaymentStatus.Refunded)]
    [TestCase(PaymentStatus.Cancelled)]
    public async Task Handle_WhenThePaymentIsNotPending_IsAConflict_AndChargesNothing(PaymentStatus status)
    {
        var seeded = await SeedAsync(status);

        var result = await Handler().Handle(new CreatePaymentCommand(seeded.OrderId), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_NOT_PENDING"));
        Assert.That((await StoredAsync(seeded.OrderId)).Status, Is.EqualTo(status));
        _processor.VerifyNoOtherCalls();
        _outbox.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Handle_WhenTheSimulatorDeclines_RecordsFailed_AndAnnouncesIt()
    {
        var seeded = await SeedAsync();
        _processor.Setup(x => x.ProcessPaymentAsync(seeded.OrderId, 100m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Failed("Card declined"));

        var result = await Handler().Handle(new CreatePaymentCommand(seeded.OrderId), CancellationToken.None);

        Assert.That(result.Value!.Status, Is.EqualTo("FAILED"));
        Assert.That((await StoredAsync(seeded.OrderId)).ErrorMessage, Is.EqualTo("Card declined"));
        _outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Once);
    }
}
