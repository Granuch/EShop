using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.UnitTests.Products;

/// <summary>
/// <c>Product.UpdateDetails</c> and <c>Product.ChangeCategory</c> (Admin panel S2). Before these
/// existed, no endpoint in the system could change a product's name, description, SKU or category
/// after creation.
/// </summary>
[TestFixture]
public class ProductUpdateDetailsTests
{
    private static Product NewProduct(string? description = null)
        => Product.Create("Original", "SKU-ORIG", 10m, 5, Guid.NewGuid(), description);

    [Test]
    public void UpdateDetails_ReplacesNameAndSku_AndTrimsThem()
    {
        var product = NewProduct();

        product.UpdateDetails("  New name  ", null, "  SKU-NEW  ");

        Assert.Multiple(() =>
        {
            Assert.That(product.Name, Is.EqualTo("New name"));
            Assert.That(product.Sku, Is.EqualTo("SKU-NEW"));
        });
    }

    // The BUG-09 three-case contract. Two of these three cases are the ones a reviewer is most
    // likely to "simplify" away by assigning unconditionally.
    [Test]
    public void UpdateDetails_WithNullDescription_LeavesTheStoredOne()
    {
        var product = NewProduct("Kept");

        product.UpdateDetails("New name", null, "SKU-ORIG");

        Assert.That(product.Description, Is.EqualTo("Kept"));
    }

    [Test]
    public void UpdateDetails_WithBlankDescription_ClearsIt()
    {
        var product = NewProduct("Removed");

        product.UpdateDetails("New name", "   ", "SKU-ORIG");

        Assert.That(product.Description, Is.Null);
    }

    [Test]
    public void UpdateDetails_WithADescription_ReplacesIt_Trimmed()
    {
        var product = NewProduct("Old");

        product.UpdateDetails("New name", "  Fresh  ", "SKU-ORIG");

        Assert.That(product.Description, Is.EqualTo("Fresh"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void UpdateDetails_WithABlankName_IsRefused(string name)
    {
        var product = NewProduct();

        Assert.Throws<DomainException>(() => product.UpdateDetails(name, null, "SKU-ORIG"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void UpdateDetails_WithABlankSku_IsRefused(string sku)
    {
        var product = NewProduct();

        Assert.Throws<DomainException>(() => product.UpdateDetails("Name", null, sku));
    }

    [Test]
    public void UpdateDetails_OnADeletedProduct_IsRefused()
    {
        var product = NewProduct();
        product.SoftDelete();

        Assert.Throws<DomainException>(() => product.UpdateDetails("Name", null, "SKU-X"));
    }

    [Test]
    public void ChangeCategory_MovesTheProduct()
    {
        var product = NewProduct();
        var target = Guid.NewGuid();

        product.ChangeCategory(target);

        Assert.That(product.CategoryId, Is.EqualTo(target));
    }

    [Test]
    public void ChangeCategory_ToTheSameCategory_IsANoOp()
    {
        var product = NewProduct();
        var original = product.CategoryId;

        product.ChangeCategory(original);

        Assert.That(product.CategoryId, Is.EqualTo(original));
    }

    [Test]
    public void ChangeCategory_ToAnEmptyId_IsRefused()
    {
        var product = NewProduct();

        Assert.Throws<DomainException>(() => product.ChangeCategory(Guid.Empty));
    }

    [Test]
    public void ChangeCategory_OnADeletedProduct_IsRefused()
    {
        var product = NewProduct();
        product.SoftDelete();

        Assert.Throws<DomainException>(() => product.ChangeCategory(Guid.NewGuid()));
    }

    [Test]
    public void UpdateDetails_RaisesNoDomainEvent()
    {
        // Deliberate: nothing consumes a "product details changed" event, and Catalog Stage 7
        // deleted the stock events precisely because an event with no handler is an outbox row
        // dispatched to nobody. Note the consequence — Basket stores a product's name on a basket
        // item at add time and will keep showing the old one.
        var product = NewProduct();
        product.ClearDomainEvents();

        product.UpdateDetails("New name", "New description", "SKU-NEW");
        product.ChangeCategory(Guid.NewGuid());

        Assert.That(product.DomainEvents, Is.Empty);
    }
}
