using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.UnitTests.Products;

/// <summary>
/// <c>Product.Restore</c> and <c>Product.AdjustStock</c> (Admin panel S4). Soft delete was a
/// one-way door — nothing in the system could undo it — and stock could only be set absolutely,
/// through the full product update that also rewrites price.
/// </summary>
[TestFixture]
public class ProductLifecycleTests
{
    private static Product NewProduct(int stock = 10)
        => Product.Create("Original", "SKU-ORIG", 10m, stock, Guid.NewGuid());

    #region Restore

    [Test]
    public void Restore_ClearsTheDeletedFlagAndTimestamp()
    {
        var product = NewProduct();
        product.SoftDelete();

        product.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(product.IsDeleted, Is.False);
            Assert.That(product.DeletedAt, Is.Null);
        });
    }

    [Test]
    public void Restore_ReturnsToDraft_NotToActive()
    {
        // The decision that matters: SoftDelete overwrites Status with Discontinued and keeps no
        // record of what it was, so the pre-deletion status cannot be recovered. Draft is the safe
        // reading — restoring must never silently put a product back in front of customers.
        var product = NewProduct();
        product.Publish();
        Assert.That(product.Status, Is.EqualTo(ProductStatus.Active), "precondition");

        product.SoftDelete();
        product.Restore();

        Assert.That(product.Status, Is.EqualTo(ProductStatus.Draft));
    }

    [Test]
    public void Restore_OnALiveProduct_IsANoOp()
    {
        // Mirrors SoftDelete, which returns early when already deleted.
        var product = NewProduct();
        product.Publish();

        product.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(product.IsDeleted, Is.False);
            Assert.That(product.Status, Is.EqualTo(ProductStatus.Active),
                "a no-op must not quietly demote a live product to Draft");
        });
    }

    [Test]
    public void Restore_MakesTheProductEditableAgain()
    {
        // Every mutator refuses a deleted product, so this is what "restored" has to mean.
        var product = NewProduct();
        product.SoftDelete();
        Assert.Throws<DomainException>(() => product.UpdateStock(5), "precondition");

        product.Restore();

        Assert.DoesNotThrow(() => product.UpdateStock(5));
    }

    [Test]
    public void Restore_RaisesNoDomainEvent()
    {
        var product = NewProduct();
        product.SoftDelete();
        product.ClearDomainEvents();

        product.Restore();

        Assert.That(product.DomainEvents, Is.Empty);
    }

    #endregion

    #region AdjustStock

    [Test]
    public void AdjustStock_AddsAndReturnsTheNewQuantity()
    {
        var product = NewProduct(stock: 10);

        var result = product.AdjustStock(5);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(15));
            Assert.That(product.StockQuantity, Is.EqualTo(15));
        });
    }

    [Test]
    public void AdjustStock_SubtractsOnANegativeDelta()
    {
        var product = NewProduct(stock: 10);

        product.AdjustStock(-3);

        Assert.That(product.StockQuantity, Is.EqualTo(7));
    }

    [Test]
    public void AdjustStock_ToExactlyZero_IsAllowed()
    {
        var product = NewProduct(stock: 4);

        product.AdjustStock(-4);

        Assert.That(product.StockQuantity, Is.EqualTo(0), "selling out is not an error");
    }

    [Test]
    public void AdjustStock_BelowZero_IsRefused_AndChangesNothing()
    {
        var product = NewProduct(stock: 4);

        Assert.Throws<DomainException>(() => product.AdjustStock(-5));
        Assert.That(product.StockQuantity, Is.EqualTo(4));
    }

    [Test]
    public void AdjustStock_ByZero_IsRefused()
    {
        // Not a no-op: a zero delta always means the caller computed it, and answering success to a
        // movement that moved nothing hides that.
        var product = NewProduct();

        Assert.Throws<DomainException>(() => product.AdjustStock(0));
    }

    [Test]
    public void AdjustStock_ThatWouldOverflowInt_IsRefused_AndChangesNothing()
    {
        // The reason the sum is computed as a long. int.MaxValue + 1 wraps to int.MinValue, which
        // would pass a plain `< 0` check on the wrapped value in an unchecked context and store a
        // hugely negative stock.
        var product = NewProduct(stock: int.MaxValue - 1);

        Assert.Throws<DomainException>(() => product.AdjustStock(1000));
        Assert.That(product.StockQuantity, Is.EqualTo(int.MaxValue - 1));
    }

    [Test]
    public void AdjustStock_LargeNegativeDelta_IsRefused_WithoutUnderflowing()
    {
        var product = NewProduct(stock: 5);

        Assert.Throws<DomainException>(() => product.AdjustStock(int.MinValue));
        Assert.That(product.StockQuantity, Is.EqualTo(5));
    }

    [Test]
    public void AdjustStock_OnADeletedProduct_IsRefused()
    {
        var product = NewProduct();
        product.SoftDelete();

        Assert.Throws<DomainException>(() => product.AdjustStock(1));
    }

    [Test]
    public void AdjustStock_Composes_WhereTwoAbsoluteWritesWouldLoseOne()
    {
        // Why the relative form exists at all. Two deliveries computed from the same starting read
        // both land; the absolute equivalent (UpdateStock(10 + 5) twice) would store 15, not 20.
        var product = NewProduct(stock: 10);

        product.AdjustStock(5);
        product.AdjustStock(5);

        Assert.That(product.StockQuantity, Is.EqualTo(20));
    }

    [Test]
    public void AdjustStock_RaisesNoDomainEvent()
    {
        // Matches UpdateStock: Catalog Stage 7 deleted the stock events because nothing consumed
        // them, and an event with no handler is an outbox row dispatched to nobody.
        var product = NewProduct();
        product.ClearDomainEvents();

        product.AdjustStock(3);

        Assert.That(product.DomainEvents, Is.Empty);
    }

    #endregion
}
