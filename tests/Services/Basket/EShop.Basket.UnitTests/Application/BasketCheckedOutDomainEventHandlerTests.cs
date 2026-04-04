using EShop.Basket.Application.EventHandlers;
using EShop.Basket.Domain.Events;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class BasketCheckedOutDomainEventHandlerTests
{
    [Test]
    public async Task Handle_ShouldMapDomainEventToIntegrationEventAndEnqueue()
    {
        var outbox = new Mock<IIntegrationEventOutbox>();
        var currentUser = new Mock<ICurrentUserContext>();
        var logger = new Mock<ILogger<BasketCheckedOutDomainEventHandler>>();

        currentUser.SetupGet(x => x.CorrelationId).Returns("corr-1");

        var handler = new BasketCheckedOutDomainEventHandler(outbox.Object, currentUser.Object, logger.Object);

        var domainEvent = new BasketCheckedOutDomainEvent
        {
            UserId = "user-1",
            Items =
            [
                new BasketCheckedOutDomainEventItem
                {
                    ProductId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    ProductName = "Product",
                    Price = 12.5m,
                    Quantity = 2
                }
            ],
            TotalPrice = 25m,
            ShippingAddress = "Street 1, City",
            PaymentMethod = "Card"
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        outbox.Verify(x => x.Enqueue(
            It.Is<BasketCheckedOutEvent>(e =>
                e.UserId == "user-1"
                && e.TotalPrice == 25m
                && e.Items.Count == 1
                && e.Items[0].ProductName == "Product"
                && e.ShippingAddress == "Street 1, City"
                && e.PaymentMethod == "Card"
                && e.CorrelationId == "corr-1"),
            "corr-1"), Times.Once);
    }
}
