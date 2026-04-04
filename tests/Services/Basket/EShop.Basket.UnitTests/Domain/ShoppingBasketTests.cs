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

    [Test]
    public void AddItem_WhenQuantityIsInvalid_ShouldThrow()
    {
        var basket = ShoppingBasket.Create("user-1");

        Assert.Throws<EShop.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            basket.AddItem(Guid.NewGuid(), "Product", 10m, 0));
    }

    [Test]
    public void Checkout_WhenShippingAddressIsMissing_ShouldThrow()
    {
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(Guid.NewGuid(), "Product", 10m, 1);

        Assert.Throws<EShop.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            basket.Checkout(string.Empty, "Card"));
    }

    [Test]
    public void ApplyPriceChange_WhenItemExists_ShouldUpdateItemPrice()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Product", 10m, 2);

        basket.ApplyPriceChange(productId, 12m);

        Assert.That(basket.Items.Single().Price, Is.EqualTo(12m));
        Assert.That(basket.TotalPrice, Is.EqualTo(24m));
    }

    [Test]
    public void Rehydrate_ShouldPreserveMetadataAndItems()
    {
        var createdAt = DateTime.UtcNow.AddDays(-2);
        var lastModifiedAt = DateTime.UtcNow.AddHours(-3);
        var productId = Guid.NewGuid();

        var basket = ShoppingBasket.Rehydrate(
            "user-1",
            createdAt,
            lastModifiedAt,
            [(productId, "Product", 10m, 2)]);

        Assert.That(basket.UserId, Is.EqualTo("user-1"));
        Assert.That(basket.CreatedAt, Is.EqualTo(createdAt));
        Assert.That(basket.LastModifiedAt, Is.EqualTo(lastModifiedAt));
        Assert.That(basket.Items, Has.Count.EqualTo(1));
        Assert.That(basket.Items.Single().ProductId, Is.EqualTo(productId));
    }
}
