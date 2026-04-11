using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Payments.Commands.CreatePayment;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

[TestFixture]
public class CreatePaymentCommandHandlerTests
{
    private static PaymentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PaymentDbContext(options);
    }

    [Test]
    public async Task Handle_WhenOrderAlreadyExists_ShouldReturnFailure()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        await repository.AddAsync(new EShop.Payment.Domain.Entities.PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            UserId = "user-1",
            Amount = 10m,
            Status = EShop.Payment.Domain.Entities.PaymentStatus.Success,
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            repository,
            Mock.Of<IPaymentProcessor>(),
            Mock.Of<IIntegrationEventOutbox>(),
            dbContext);

        var result = await handler.Handle(new CreatePaymentCommand(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "user-1",
            50m,
            "USD",
            "Mock"),
            CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("PAYMENT_ALREADY_EXISTS"));
    }

    [Test]
    public async Task Handle_WhenProcessorSucceeds_ShouldReturnSuccess()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var processor = new Mock<IPaymentProcessor>();
        processor.Setup(x => x.ProcessPaymentAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentResult.Successful("pi_handler"));

        var outbox = new Mock<IIntegrationEventOutbox>();

        var handler = new CreatePaymentCommandHandler(
            repository,
            processor.Object,
            outbox.Object,
            dbContext);

        var result = await handler.Handle(new CreatePaymentCommand(
            Guid.NewGuid(),
            "user-1",
            50m,
            "USD",
            "Mock"),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo("SUCCESS"));
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCreatedEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentSuccessEvent>(), It.IsAny<string?>()), Times.Once);
        outbox.Verify(x => x.Enqueue(It.IsAny<PaymentCompletedEvent>(), It.IsAny<string?>()), Times.Once);
    }
}
