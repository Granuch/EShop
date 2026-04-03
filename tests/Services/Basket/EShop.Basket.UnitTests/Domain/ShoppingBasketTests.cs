using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Events;

namespace EShop.Basket.UnitTests.Domain;

[TestFixture]
public class ShoppingBasketTests
{
    [Test]
    public void AddItem_WhenProductAlreadyExists_ShouldMergeQuantities()
    {
        var basket = ShoppingBasket.Create("user-1");

        basket.AddItem(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Product", 10m, 1);
        basket.AddItem(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Product", 10m, 2);

        Assert.That(basket.Items, Has.Count.EqualTo(1));
        Assert.That(basket.Items.Single().Quantity, Is.EqualTo(3));
    }

    [Test]
    public void UpdateItemQuantity_WhenQuantityIsZero_ShouldRemoveItem()
    {
        var basket = ShoppingBasket.Create("user-1");
        var productId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        basket.AddItem(productId, "Product", 10m, 2);
        basket.UpdateItemQuantity(productId, 0);

        Assert.That(basket.Items, Is.Empty);
    }

    [Test]
    public void Checkout_WhenBasketHasItems_ShouldRaiseDomainEvent()
    {
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Product", 10m, 2);

        basket.Checkout("Street 1, City", "CreditCard");

        var checkoutEvent = basket.DomainEvents.OfType<BasketCheckedOutDomainEvent>().SingleOrDefault();
        Assert.That(checkoutEvent, Is.Not.Null);
        Assert.That(checkoutEvent!.UserId, Is.EqualTo("user-1"));
        Assert.That(checkoutEvent.Items, Has.Count.EqualTo(1));
    }
}
