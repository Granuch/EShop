using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
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
            dbContext);

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
            dbContext);

        var result = await handler.Handle(new CreatePaymentIntentCommand(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user-1",
            42m,
            "USD",
            null), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_ALREADY_EXISTS"));
    }
}
