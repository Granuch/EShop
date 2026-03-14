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

    [Test]
    public void Constructor_WithZeroQuantity_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.NewGuid(), "Widget A", 10.00m, 0));
    }

    [Test]
    public void Constructor_WithNegativeQuantity_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.NewGuid(), "Widget A", 10.00m, -1));
    }

    [Test]
    public void Constructor_WithNegativePrice_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.NewGuid(), "Widget A", -5.00m, 1));
    }

    [Test]
    public void Constructor_WithEmptyProductName_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.NewGuid(), "", 10.00m, 1));
    }

    [Test]
    public void Constructor_WithWhitespaceProductName_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new OrderItem(Guid.NewGuid(), "   ", 10.00m, 1));
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
