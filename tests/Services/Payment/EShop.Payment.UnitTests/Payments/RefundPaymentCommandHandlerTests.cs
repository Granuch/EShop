using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Commands.RefundPayment;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

[TestFixture]
public class RefundPaymentCommandHandlerTests
{
    private static PaymentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PaymentDbContext(options);
    }

    private static RefundPaymentCommandHandler CreateHandler(
        PaymentDbContext dbContext,
        IPaymentProcessor? processor = null,
        IStripePaymentService? stripePaymentService = null,
        IIntegrationEventOutbox? outbox = null)
    {
        var repository = new PaymentRepository(dbContext);
        return new RefundPaymentCommandHandler(
            repository,
            processor ?? Mock.Of<IPaymentProcessor>(),
            stripePaymentService ?? Mock.Of<IStripePaymentService>(),
            outbox ?? Mock.Of<IIntegrationEventOutbox>(),
            dbContext,
            Mock.Of<ILogger<RefundPaymentCommandHandler>>());
    }

    private static async Task<PaymentTransaction> CreateAndAddStripePaymentAsync(PaymentDbContext dbContext, string paymentIntentId = "pi_test")
    {
        var repository = new PaymentRepository(dbContext);
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = "Stripe",
            PaymentIntentId = paymentIntentId,
            Status = PaymentStatus.Success,
            CreatedAt = DateTime.UtcNow
        };
        await repository.AddAsync(payment);
        await dbContext.SaveChangesAsync();
        return payment;
    }

    [Test]
    public async Task Handle_WhenPaymentNotFound_ShouldReturnFailure()
    {
        await using var dbContext = CreateDbContext();

        var handler = CreateHandler(dbContext);

        var result = await handler.Handle(new RefundPaymentCommand(Guid.NewGuid(), 10m, "test"), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_NOT_FOUND"));
    }

    [Test]
    public async Task Handle_WhenSuccessful_ShouldSetRefundedStatus()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = "Mock",
            PaymentIntentId = "pi_test",
            Status = PaymentStatus.Success,
            CreatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(payment);
        await dbContext.SaveChangesAsync();

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.RefundPaymentAsync("pi_test", 20m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Successful("pi_test"));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var handler = new RefundPaymentCommandHandler(
            repository,
            processor.Object,
            Mock.Of<IStripePaymentService>(),
            outbox.Object,
            dbContext,
            Mock.Of<ILogger<RefundPaymentCommandHandler>>());

        var result = await handler.Handle(new RefundPaymentCommand(payment.Id, 20m, "customer request"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo("REFUNDED"));
        outbox.Verify(x => x.Enqueue(It.IsAny<EShop.BuildingBlocks.Messaging.Events.PaymentRefundedEvent>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenStripeRefundSucceeds_ShouldSetRefundedStatusAndEnqueueEvent()
    {
        await using var dbContext = CreateDbContext();
        var payment = await CreateAndAddStripePaymentAsync(dbContext, "pi_stripe_001");

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(x => x.CreateRefundAsync("pi_stripe_001", 50m, "USD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("re_test", "succeeded"));

        var outbox = new Mock<IIntegrationEventOutbox>();
        var handler = CreateHandler(dbContext, stripePaymentService: stripePaymentService.Object, outbox: outbox.Object);

        var result = await handler.Handle(new RefundPaymentCommand(payment.Id, 50m, "test"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo("REFUNDED"));
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentRefundedEvent>(), It.IsAny<string?>()), Times.Once);
    }

    [TestCase("failed")]
    [TestCase("canceled")]
    public async Task Handle_WhenStripeRefundHasFailedStatus_ShouldReturnFailureAndNotMarkRefunded(string stripeStatus)
    {
        await using var dbContext = CreateDbContext();
        var payment = await CreateAndAddStripePaymentAsync(dbContext, "pi_stripe_002");

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(x => x.CreateRefundAsync("pi_stripe_002", 100m, "USD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeRefundResult("re_test", stripeStatus));

        var outbox = new Mock<IIntegrationEventOutbox>();
        var handler = CreateHandler(dbContext, stripePaymentService: stripePaymentService.Object, outbox: outbox.Object);

        var result = await handler.Handle(new RefundPaymentCommand(payment.Id, 100m, "test"), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("REFUND_FAILED"));
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentRefundedEvent>(), It.IsAny<string?>()), Times.Never);

        var updatedPayment = dbContext.PaymentTransactions.Single(p => p.Id == payment.Id);
        Assert.That(updatedPayment.Status, Is.EqualTo(PaymentStatus.Success));
    }

    [Test]
    public async Task Handle_WhenStripeRefundThrows_ShouldReturnSanitizedFailureAndNotMarkRefunded()
    {
        await using var dbContext = CreateDbContext();
        var payment = await CreateAndAddStripePaymentAsync(dbContext, "pi_stripe_003");

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(x => x.CreateRefundAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Internal Stripe secret details"));

        var outbox = new Mock<IIntegrationEventOutbox>();
        var handler = CreateHandler(dbContext, stripePaymentService: stripePaymentService.Object, outbox: outbox.Object);

        var result = await handler.Handle(new RefundPaymentCommand(payment.Id, 100m, "test"), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("REFUND_FAILED"));
        Assert.That(result.Error.Message, Does.Not.Contain("Internal Stripe secret details"));
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentRefundedEvent>(), It.IsAny<string?>()), Times.Never);

        var updatedPayment = dbContext.PaymentTransactions.Single(p => p.Id == payment.Id);
        Assert.That(updatedPayment.Status, Is.EqualTo(PaymentStatus.Success));
    }
}
