using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.Basket.Domain.Events;
using EShop.Basket.Domain.ValueObjects;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Basket.UnitTests.Application;

[TestFixture]
public class BasketCheckedOutEventMapperTests
{
    private static BasketCheckedOutDomainEvent DomainEvent() => new()
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

    /// <summary>Basket audit D3: one id from the client's checkoutId to Ordering's MessageId.</summary>
    [Test]
    public void TheIntegrationEvent_TakesTheDomainEventsIdAndTime()
    {
        var domainEvent = DomainEvent();

        var integrationEvent = BasketCheckedOutEventMapper.ToIntegrationEvent(domainEvent, "corr-1");

        Assert.That(integrationEvent.EventId, Is.EqualTo(domainEvent.EventId));
        Assert.That(integrationEvent.OccurredOn, Is.EqualTo(domainEvent.OccurredOn));
    }

    /// <summary>
    /// Ordering reads only ShippingAddressDetails and rejects a message without it (Ordering audit C2), so the
    /// structured form is the part of this mapping that must never go missing.
    /// </summary>
    [Test]
    public void TheIntegrationEvent_CarriesTheLinesTheTotalAndTheStructuredAddress()
    {
        var integrationEvent = BasketCheckedOutEventMapper.ToIntegrationEvent(DomainEvent(), "corr-1");

        Assert.That(integrationEvent.UserId, Is.EqualTo("user-1"));
        Assert.That(integrationEvent.TotalPrice, Is.EqualTo(25m));
        Assert.That(integrationEvent.Items, Has.Count.EqualTo(1));
        Assert.That(integrationEvent.Items[0].ProductName, Is.EqualTo("Product"));
        Assert.That(integrationEvent.Items[0].Price, Is.EqualTo(12.5m));
        Assert.That(integrationEvent.Items[0].Quantity, Is.EqualTo(2));
        Assert.That(integrationEvent.PaymentMethod, Is.EqualTo("Card"));
        Assert.That(integrationEvent.CorrelationId, Is.EqualTo("corr-1"));
        Assert.That(integrationEvent.ShippingAddressDetails, Is.EqualTo(new CheckoutShippingAddress
        {
            Street = "1 Main St",
            City = "Kyiv",
            State = "Kyiv",
            ZipCode = "01001",
            Country = "UA"
        }));
        Assert.That(integrationEvent.ShippingAddress, Is.EqualTo("1 Main St, Kyiv, Kyiv 01001, UA"));
    }
}
