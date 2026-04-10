using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

[TestFixture]
public class CreatePaymentIntentCommandHandlerTests
{
    private static PaymentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PaymentDbContext(options);
    }

    private static CreatePaymentIntentCommandHandler CreateHandler(
        PaymentDbContext dbContext,
        IStripeCustomerService? stripeCustomerService = null,
        IStripePaymentService? stripePaymentService = null,
        IIntegrationEventOutbox? outbox = null)
    {
        var repository = new PaymentRepository(dbContext);
        return new CreatePaymentIntentCommandHandler(
            repository,
            stripeCustomerService ?? Mock.Of<IStripeCustomerService>(),
            stripePaymentService ?? Mock.Of<IStripePaymentService>(),
            outbox ?? Mock.Of<IIntegrationEventOutbox>(),
            dbContext,
            Mock.Of<ILogger<CreatePaymentIntentCommandHandler>>());
    }

    [Test]
    public async Task Handle_WhenStripeIntentCreated_ShouldReturnClientSecret()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var stripeCustomerService = new Mock<IStripeCustomerService>();
        stripeCustomerService
            .Setup(x => x.CreateOrGetCustomerAsync("user-1", "user@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync("cus_test_123");

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(x => x.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripePaymentIntentResult("pi_test_123", "cs_test_123", "requires_payment_method"));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var handler = new CreatePaymentIntentCommandHandler(
            repository,
            stripeCustomerService.Object,
            stripePaymentService.Object,
            outbox.Object,
            dbContext,
            Mock.Of<ILogger<CreatePaymentIntentCommandHandler>>());

        var result = await handler.Handle(new CreatePaymentIntentCommand(
            Guid.NewGuid(),
            "user-1",
            42m,
            "USD",
            "user@test.com"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ClientSecret, Is.EqualTo("cs_test_123"));
        Assert.That(result.Value.PaymentIntentId, Is.EqualTo("pi_test_123"));

        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task Handle_WhenOrderAlreadyExists_ShouldReturnConflictError()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        await repository.AddAsync(new EShop.Payment.Domain.Entities.PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserId = "user-1",
            Amount = 10m,
            Currency = "USD",
            PaymentMethod = "Stripe",
            Status = EShop.Payment.Domain.Entities.PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var handler = new CreatePaymentIntentCommandHandler(
            repository,
            Mock.Of<IStripeCustomerService>(),
            Mock.Of<IStripePaymentService>(),
            Mock.Of<IIntegrationEventOutbox>(),
            dbContext,
            Mock.Of<ILogger<CreatePaymentIntentCommandHandler>>());

        var result = await handler.Handle(new CreatePaymentIntentCommand(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user-1",
            42m,
            "USD",
            null), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_ALREADY_EXISTS"));
    }

    [Test]
    public async Task Handle_WhenCustomerCreationFails_ShouldMarkPaymentFailedAndEnqueueFailedEvent()
    {
        await using var dbContext = CreateDbContext();

        var stripeCustomerService = new Mock<IStripeCustomerService>();
        stripeCustomerService
            .Setup(x => x.CreateOrGetCustomerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stripe customer API error"));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var handler = CreateHandler(dbContext, stripeCustomerService.Object, outbox: outbox.Object);

        var result = await handler.Handle(new CreatePaymentIntentCommand(
            Guid.NewGuid(), "user-1", 50m, "USD", "user@test.com"), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("STRIPE_PAYMENT_INTENT_FAILED"));

        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Never);

        var payment = dbContext.PaymentTransactions.Single();
        Assert.That(payment.Status, Is.EqualTo(EShop.Payment.Domain.Entities.PaymentStatus.Failed));
        Assert.That(payment.ErrorMessage, Is.EqualTo("Failed to create Stripe payment intent."));
    }

    [Test]
    public async Task Handle_WhenIntentCreationFails_ShouldMarkPaymentFailedAndEnqueueFailedEvent()
    {
        await using var dbContext = CreateDbContext();

        var stripeCustomerService = new Mock<IStripeCustomerService>();
        stripeCustomerService
            .Setup(x => x.CreateOrGetCustomerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cus_test_123");

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(x => x.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stripe payment intent API error"));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var handler = CreateHandler(dbContext, stripeCustomerService.Object, stripePaymentService.Object, outbox.Object);

        var result = await handler.Handle(new CreatePaymentIntentCommand(
            Guid.NewGuid(), "user-1", 75m, "EUR", "user@test.com"), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("STRIPE_PAYMENT_INTENT_FAILED"));

        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Never);

        var payment = dbContext.PaymentTransactions.Single();
        Assert.That(payment.Status, Is.EqualTo(EShop.Payment.Domain.Entities.PaymentStatus.Failed));
        Assert.That(payment.ErrorMessage, Is.EqualTo("Failed to create Stripe payment intent."));
    }

    [TestCase("  USD  ", "USD")]
    [TestCase("   ", "USD")]
    [TestCase(null, "USD")]
    [TestCase("eur", "EUR")]
    public async Task Handle_CurrencyNormalization_ShouldStoreNormalizedCurrency(string? inputCurrency, string expectedCurrency)
    {
        await using var dbContext = CreateDbContext();

        var stripeCustomerService = new Mock<IStripeCustomerService>();
        stripeCustomerService
            .Setup(x => x.CreateOrGetCustomerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stop early"));

        var handler = CreateHandler(dbContext, stripeCustomerService.Object);

        await handler.Handle(new CreatePaymentIntentCommand(
            Guid.NewGuid(), "user-1", 10m, inputCurrency, null), CancellationToken.None);

        var payment = dbContext.PaymentTransactions.Single();
        Assert.That(payment.Currency, Is.EqualTo(expectedCurrency));
    }
}
