using EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

[TestFixture]
public class GetPaymentsByUserQueryHandlerTests
{
    [Test]
    public async Task Handle_ShouldUseAsyncRepositoryMethodAndReturnMappedItems()
    {
        var createdAt = DateTime.UtcNow;
        var payments = new List<PaymentTransaction>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                UserId = "user-1",
                Amount = 25m,
                Currency = "USD",
                PaymentMethod = "Mock",
                Status = PaymentStatus.Success,
                CreatedAt = createdAt
            }
        };

        var repository = new Mock<IPaymentRepository>();
        repository
            .Setup(x => x.GetByUserIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments);

        var handler = new GetPaymentsByUserQueryHandler(repository.Object);

        var result = await handler.Handle(new GetPaymentsByUserQuery("user-1"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].UserId, Is.EqualTo("user-1"));
        Assert.That(result.Value[0].Status, Is.EqualTo("SUCCESS"));

        repository.Verify(x => x.GetByUserIdAsync("user-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
