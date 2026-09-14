using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Events;
using EShop.Basket.Domain.ValueObjects;
using EShop.BuildingBlocks.Domain.Exceptions;

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

        basket.Checkout(ShippingAddress.Create("1 Main St", "Springfield", "IL", "62701", "US"));

        var checkoutEvent = basket.DomainEvents.OfType<BasketCheckedOutDomainEvent>().SingleOrDefault();
        Assert.That(checkoutEvent, Is.Not.Null);
        Assert.That(checkoutEvent!.UserId, Is.EqualTo("user-1"));
        Assert.That(checkoutEvent.Items, Has.Count.EqualTo(1));
        Assert.That(checkoutEvent.ShippingAddress.City, Is.EqualTo("Springfield"));
    }

    [Test]
    public void ShippingAddress_TrimsPartsAndUpperCasesTheCountry()
    {
        var address = ShippingAddress.Create(" 1 Main St ", "Springfield", "IL", "62701", "us");

        Assert.That(address.Street, Is.EqualTo("1 Main St"));
        Assert.That(address.Country, Is.EqualTo("US"));
        Assert.That(address.ToString(), Is.EqualTo("1 Main St, Springfield, IL 62701, US"));
    }

    [Test]
    public void ShippingAddress_WithAMissingPart_ShouldThrow()
    {
        Assert.Throws<EShop.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            ShippingAddress.Create("1 Main St", "Springfield", "IL", " ", "US"));
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
            basket.Checkout(null!));
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
            [new StoredBasketItem(productId, "Product", 10m, 2)]);

        Assert.That(basket.UserId, Is.EqualTo("user-1"));
        Assert.That(basket.CreatedAt, Is.EqualTo(createdAt));
        Assert.That(basket.LastModifiedAt, Is.EqualTo(lastModifiedAt));
        Assert.That(basket.Items, Has.Count.EqualTo(1));
        Assert.That(basket.Items.Single().ProductId, Is.EqualTo(productId));
    }

    /// <summary>Basket audit M1: adding a product again takes the name and price Catalog just gave.</summary>
    [Test]
    public void AddItem_WhenProductAlreadyExists_TakesTheCurrentNameAndPrice()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Old name", 10m, 1);

        basket.AddItem(productId, "New name", 8m, 2);

        var item = basket.Items.Single();
        Assert.That(item.ProductName, Is.EqualTo("New name"));
        Assert.That(item.Price, Is.EqualTo(8m));
        Assert.That(item.Quantity, Is.EqualTo(3));
    }

    [Test]
    public void ApplyPriceChange_ReportsWhetherTheBasketChanged()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Product", 10m, 1);

        Assert.That(basket.ApplyPriceChange(productId, 12m), Is.True);
        Assert.That(basket.ApplyPriceChange(productId, 12m), Is.False, "already at that price");
        Assert.That(basket.ApplyPriceChange(Guid.NewGuid(), 12m), Is.False, "not in the basket");
    }

    /// <summary>Basket audit S9 (M2): a line holds at most MaxQuantityPerLine, however it gets there.</summary>
    [Test]
    public void AddItem_BeyondTheLineLimit_ShouldThrow_AndLeaveTheLine()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");

        Assert.Throws<DomainException>(() => basket.AddItem(productId, "Product", 10m, ShoppingBasket.MaxQuantityPerLine + 1));

        basket.AddItem(productId, "Product", 10m, ShoppingBasket.MaxQuantityPerLine - 1);
        basket.AddItem(productId, "Product", 10m, 1);
        Assert.Throws<DomainException>(() => basket.AddItem(productId, "Product", 10m, 1), "the merged total counts");
        Assert.That(basket.Items.Single().Quantity, Is.EqualTo(ShoppingBasket.MaxQuantityPerLine));
    }

    /// <summary>
    /// A line stored before the limit can be near int.MaxValue. Merging used to be an int sum, so adding to it wrapped
    /// around to a negative quantity instead of being refused.
    /// </summary>
    [Test]
    public void AddItem_ToALineStoredBeforeTheLimit_IsRefused_WithoutOverflowing()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Rehydrate(
            "user-1", DateTime.UtcNow, DateTime.UtcNow, [new StoredBasketItem(productId, "Product", 10m, int.MaxValue)]);

        var refused = Assert.Throws<DomainException>(() => basket.AddItem(productId, "Product", 10m, 1));

        Assert.That(refused!.Message, Does.Contain(ShoppingBasket.MaxQuantityPerLine.ToString()));
        Assert.That(basket.Items.Single().Quantity, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void UpdateItemQuantity_BeyondTheLineLimit_ShouldThrow()
    {
        var productId = Guid.NewGuid();
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(productId, "Product", 10m, 1);

        Assert.Throws<DomainException>(() => basket.UpdateItemQuantity(productId, ShoppingBasket.MaxQuantityPerLine + 1));
        Assert.That(basket.Items.Single().Quantity, Is.EqualTo(1));
    }

    [Test]
    public void AddItem_BeyondTheLineCount_ShouldThrow_ButAnExistingProductCanStillBeAdded()
    {
        var basket = ShoppingBasket.Create("user-1");
        for (var i = 0; i < ShoppingBasket.MaxLines; i++)
        {
            basket.AddItem(Guid.NewGuid(), "Product", 1m, 1);
        }

        Assert.Throws<DomainException>(() => basket.AddItem(Guid.NewGuid(), "One too many", 1m, 1));

        basket.AddItem(basket.Items.First().ProductId, "Product", 1m, 1);
        Assert.That(basket.Items, Has.Count.EqualTo(ShoppingBasket.MaxLines));
    }

    /// <summary>
    /// Basket audit M2: two lines of 1.5 billion overflowed the checked int Sum, and that basket's GET failed for good.
    /// </summary>
    [Test]
    public void TotalItems_OfABasketStoredBeforeTheLimit_DoesNotOverflow()
    {
        var basket = ShoppingBasket.Rehydrate("user-1", DateTime.UtcNow, DateTime.UtcNow,
        [
            new StoredBasketItem(Guid.NewGuid(), "A", 1m, 1_500_000_000),
            new StoredBasketItem(Guid.NewGuid(), "B", 1m, 1_500_000_000)
        ]);

        Assert.That(basket.TotalItems, Is.EqualTo(3_000_000_000L));
    }

    /// <summary>Basket audit L1 (S9).</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithoutAUserId_ShouldThrow(string userId)
    {
        Assert.Throws<DomainException>(() => ShoppingBasket.Create(userId));
    }

    /// <summary>Basket audit L1 (S9): checking out changes state, so it cannot raise a second checkout event.</summary>
    [Test]
    public void Checkout_Twice_ShouldThrow_AndRaiseOneEvent()
    {
        var basket = ShoppingBasket.Create("user-1");
        basket.AddItem(Guid.NewGuid(), "Product", 10m, 1);
        var address = ShippingAddress.Create("1 Main St", "Springfield", "IL", "62701", "US");
        basket.Checkout(address);

        Assert.Throws<DomainException>(() => basket.Checkout(address));
        Assert.Throws<DomainException>(() => basket.AddItem(Guid.NewGuid(), "After checkout", 1m, 1));
        Assert.That(basket.IsCheckedOut, Is.True);
        Assert.That(basket.DomainEvents.OfType<BasketCheckedOutDomainEvent>().Count(), Is.EqualTo(1));
    }

    /// <summary>Basket audit L1 (S9): a line's identity is its product, and when it was added survives a read.</summary>
    [Test]
    public void ALine_IsIdentifiedByItsProduct_AndKeepsWhenItWasAdded()
    {
        var productId = Guid.NewGuid();
        var addedAt = DateTime.UtcNow.AddDays(-1);
        var createdAt = DateTime.UtcNow.AddDays(-2);
        var oldLine = Guid.NewGuid();

        var basket = ShoppingBasket.Rehydrate("user-1", createdAt, createdAt,
        [
            new StoredBasketItem(productId, "Product", 10m, 1, addedAt),
            new StoredBasketItem(oldLine, "Stored before S9", 10m, 1)
        ]);

        var line = basket.Items.Single(i => i.ProductId == productId);
        Assert.That(line.Id, Is.EqualTo(productId));
        Assert.That(line.CreatedAt, Is.EqualTo(addedAt));
        Assert.That(basket.Items.Single(i => i.ProductId == oldLine).CreatedAt, Is.EqualTo(createdAt),
            "a line stored without its time takes the basket's");
    }

    /// <summary>Basket audit L1 (S9): LastModifiedAt is the base class's UpdatedAt, not a second field.</summary>
    [Test]
    public void LastModifiedAt_IsUpdatedAt()
    {
        var basket = ShoppingBasket.Create("user-1");
        Assert.That(basket.LastModifiedAt, Is.EqualTo(basket.CreatedAt), "an unchanged basket");

        basket.AddItem(Guid.NewGuid(), "Product", 10m, 1);

        Assert.That(basket.UpdatedAt, Is.Not.Null);
        Assert.That(basket.LastModifiedAt, Is.EqualTo(basket.UpdatedAt));
    }
}
