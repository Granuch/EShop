using EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>Payment audit Stage 10 (M7, D10): the per-user list is one page, with its total.</summary>
[TestFixture]
public class GetPaymentsByUserQueryHandlerTests
{
    [Test]
    public async Task Handle_ReturnsTheRepositorysPage_WithItsTotal()
    {
        var payments = new List<PaymentTransaction>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                UserId = "user-1",
                Amount = 25m,
                Currency = "USD",
                PaymentMethod = PaymentMethodType.Mock,
                Status = PaymentStatus.Success,
                CreatedAt = DateTime.UtcNow
            }
        };

        var repository = new Mock<IPaymentRepository>();
        repository
            .Setup(x => x.GetPageByUserIdAsync("user-1", 2, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((payments, 7));

        var result = await new GetPaymentsByUserQueryHandler(repository.Object).Handle(
            new GetPaymentsByUserQuery { UserId = "user-1", PageNumber = 2, PageSize = 5 },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var page = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(page.Items.Single().UserId, Is.EqualTo("user-1"));
            Assert.That(page.Items.Single().Status, Is.EqualTo("SUCCESS"));
            Assert.That(page.TotalCount, Is.EqualTo(7));
            Assert.That(page.PageNumber, Is.EqualTo(2));
            Assert.That(page.PageSize, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Handle_WithNoPageGiven_AsksForTheFirstTen()
    {
        var repository = new Mock<IPaymentRepository>();
        repository
            .Setup(x => x.GetPageByUserIdAsync("user-1", 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PaymentTransaction>(), 0));

        await new GetPaymentsByUserQueryHandler(repository.Object).Handle(
            new GetPaymentsByUserQuery { UserId = "user-1" },
            CancellationToken.None);

        repository.Verify(x => x.GetPageByUserIdAsync("user-1", 1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
