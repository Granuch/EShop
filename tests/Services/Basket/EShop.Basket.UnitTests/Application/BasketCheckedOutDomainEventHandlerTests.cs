using EShop.Basket.Application.EventHandlers;
using EShop.Basket.Domain.Events;
using EShop.Basket.Domain.ValueObjects;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class BasketCheckedOutDomainEventHandlerTests
{
    /// <summary>
    /// Ordering reads only ShippingAddressDetails and rejects a message without it (Ordering audit C2),
    /// so the structured form is the part of this mapping that must never go missing.
    /// </summary>
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
            ShippingAddress = ShippingAddress.Create("1 Main St", "Kyiv", "Kyiv", "01001", "UA"),
            PaymentMethod = "Card"
        };

        BasketCheckedOutEvent? enqueued = null;
        outbox
            .Setup(x => x.Enqueue(It.IsAny<BasketCheckedOutEvent>(), "corr-1"))
            .Callback<IIntegrationEvent, string?>((e, _) => enqueued = (BasketCheckedOutEvent)e);

        await handler.Handle(domainEvent, CancellationToken.None);

        Assert.That(enqueued, Is.Not.Null);
        Assert.That(enqueued!.UserId, Is.EqualTo("user-1"));
        Assert.That(enqueued.TotalPrice, Is.EqualTo(25m));
        Assert.That(enqueued.Items, Has.Count.EqualTo(1));
        Assert.That(enqueued.Items[0].ProductName, Is.EqualTo("Product"));
        Assert.That(enqueued.PaymentMethod, Is.EqualTo("Card"));
        Assert.That(enqueued.CorrelationId, Is.EqualTo("corr-1"));

        Assert.That(enqueued.ShippingAddressDetails, Is.EqualTo(new CheckoutShippingAddress
        {
            Street = "1 Main St",
            City = "Kyiv",
            State = "Kyiv",
            ZipCode = "01001",
            Country = "UA"
        }));
        Assert.That(enqueued.ShippingAddress, Is.EqualTo("1 Main St, Kyiv, Kyiv 01001, UA"));
    }
}
