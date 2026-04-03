using EShop.Basket.Domain.Entities;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Basket.UnitTests.Domain;

[TestFixture]
public class BasketItemTests
{
    [Test]
    public void Constructor_WhenProductIdIsEmpty_ShouldThrow()
    {
        Assert.Throws<DomainException>(() => new BasketItem(Guid.Empty, "Phone", 100m, 1));
    }

    [Test]
    public void UpdateQuantity_WhenQuantityIsZero_ShouldThrow()
    {
        var item = new BasketItem(Guid.NewGuid(), "Phone", 100m, 1);

        Assert.Throws<DomainException>(() => item.UpdateQuantity(0));
    }

    [Test]
    public void UpdatePrice_WhenPriceChanges_ShouldRecalculateSubTotal()
    {
        var item = new BasketItem(Guid.NewGuid(), "Phone", 100m, 2);

        item.UpdatePrice(120m);

        Assert.That(item.Price, Is.EqualTo(120m));
        Assert.That(item.SubTotal, Is.EqualTo(240m));
    }
}
