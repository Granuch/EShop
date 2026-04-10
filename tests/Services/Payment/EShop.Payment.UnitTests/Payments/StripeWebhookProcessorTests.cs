using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.Infrastructure.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

[TestFixture]
public class StripeWebhookProcessorTests
{
    private static PaymentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PaymentDbContext(options);
    }

    [Test]
    public async Task ProcessAsync_WhenSucceededEvent_ShouldMarkPaymentSuccess()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        await repository.AddAsync(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = "Stripe",
            PaymentIntentId = "pi_test_001",
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService.Setup(x => x.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent("evt_test_001", "payment_intent.succeeded", "pi_test_001", "succeeded", null));

        var publishEndpoint = new Mock<IPublishEndpoint>();

        var processor = new StripeWebhookProcessor(
            repository,
            stripePaymentService.Object,
            dbContext,
            publishEndpoint.Object,
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        var result = await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        Assert.That(result.IsDuplicate, Is.False);

        var updated = await repository.GetByPaymentIntentIdAsync("pi_test_001", CancellationToken.None);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Status, Is.EqualTo(PaymentStatus.Success));
        Assert.That(await repository.IsStripeEventProcessedAsync("evt_test_001", CancellationToken.None), Is.True);

        publishEndpoint.Verify(x => x.Publish(It.IsAny<EShop.BuildingBlocks.Messaging.Events.PaymentSuccessEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ProcessAsync_WhenDuplicateEvent_ShouldReturnDuplicate()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService.Setup(x => x.ConstructWebhookEvent(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new StripeWebhookEvent("evt_test_dup", "payment_intent.succeeded", "pi_missing", "succeeded", null));

        var processor = new StripeWebhookProcessor(
            repository,
            stripePaymentService.Object,
            dbContext,
            Mock.Of<IPublishEndpoint>(),
            Mock.Of<ILogger<StripeWebhookProcessor>>());

        await processor.ProcessAsync("payload", "sig", CancellationToken.None);
        var duplicate = await processor.ProcessAsync("payload", "sig", CancellationToken.None);

        Assert.That(duplicate.IsDuplicate, Is.True);
    }
}
