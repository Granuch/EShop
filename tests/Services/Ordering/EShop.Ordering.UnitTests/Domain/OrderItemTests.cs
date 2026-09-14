using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.UnitTests.Domain;

[TestFixture]
public class OrderItemTests
{
    [Test]
    public void Constructor_WithValidParameters_ShouldCreateOrderItem()
    {
        var productId = Guid.NewGuid();

        var item = new OrderItem(productId, "Widget A", 10.00m, 3);

        Assert.That(item.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(item.ProductId, Is.EqualTo(productId));
        Assert.That(item.ProductName, Is.EqualTo("Widget A"));
        Assert.That(item.UnitPrice, Is.EqualTo(10.00m));
        Assert.That(item.Quantity, Is.EqualTo(3));
        Assert.That(item.SubTotal, Is.EqualTo(30.00m));
    }

    /// <summary>Audit M1: DomainException maps to 400; these were ArgumentExceptions, i.e. 500s.</summary>
    [TestCase("Widget A", 10.00, 0)]
    [TestCase("Widget A", 10.00, -1)]
    [TestCase("Widget A", -5.00, 1)]
    [TestCase("", 10.00, 1)]
    [TestCase("   ", 10.00, 1)]
    public void Constructor_WithAnInvalidLine_ThrowsDomainException(string name, decimal price, int quantity)
    {
        Assert.Throws<DomainException>(() => new OrderItem(Guid.NewGuid(), name, price, quantity));
    }

    /// <summary>The column is varchar(200); past it the insert failed at the database, as a 500.</summary>
    [Test]
    public void Constructor_WithANameLongerThanTheColumn_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            new OrderItem(Guid.NewGuid(), new string('x', OrderItem.MaxProductNameLength + 1), 1m, 1));

        Assert.DoesNotThrow(() =>
            new OrderItem(Guid.NewGuid(), new string('x', OrderItem.MaxProductNameLength), 1m, 1));
    }

    [Test]
    public void SubTotal_ShouldBeUnitPriceTimesQuantity()
    {
        var item = new OrderItem(Guid.NewGuid(), "Widget", 7.50m, 4);

        Assert.That(item.SubTotal, Is.EqualTo(30.00m));
    }

    [Test]
    public void Constructor_WithZeroPrice_ShouldSucceed()
    {
        var item = new OrderItem(Guid.NewGuid(), "Free Item", 0m, 1);

        Assert.That(item.UnitPrice, Is.EqualTo(0m));
        Assert.That(item.SubTotal, Is.EqualTo(0m));
    }
}
