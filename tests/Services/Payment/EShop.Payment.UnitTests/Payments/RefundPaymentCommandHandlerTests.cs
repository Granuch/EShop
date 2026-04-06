using EShop.Payment.Application.Payments.Commands.RefundPayment;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
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

    [Test]
    public async Task Handle_WhenPaymentNotFound_ShouldReturnFailure()
    {
        await using var dbContext = CreateDbContext();
        var repository = new PaymentRepository(dbContext);

        var handler = new RefundPaymentCommandHandler(
            repository,
            Mock.Of<IPaymentProcessor>(),
            Mock.Of<IPublishEndpoint>(),
            dbContext);

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

        var handler = new RefundPaymentCommandHandler(
            repository,
            processor.Object,
            Mock.Of<IPublishEndpoint>(),
            dbContext);

        var result = await handler.Handle(new RefundPaymentCommand(payment.Id, 20m, "customer request"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo("REFUNDED"));
    }
}
