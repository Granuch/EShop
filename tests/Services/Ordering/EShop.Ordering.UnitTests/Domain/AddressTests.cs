using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.UnitTests.Domain;

[TestFixture]
public class AddressTests
{
    [Test]
    public void Constructor_WithValidParameters_ShouldCreateAddress()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");

        Assert.That(address.Street, Is.EqualTo("123 Main St"));
        Assert.That(address.City, Is.EqualTo("Springfield"));
        Assert.That(address.State, Is.EqualTo("IL"));
        Assert.That(address.ZipCode, Is.EqualTo("62701"));
        Assert.That(address.Country, Is.EqualTo("US"));
    }

    [Test]
    public void Constructor_WithEmptyStreet_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new Address("", "Springfield", "IL", "62701", "US"));
    }

    [Test]
    public void Constructor_WithInvalidCountryCode_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new Address("123 Main St", "Springfield", "IL", "62701", "USA"));
    }

    [Test]
    public void Constructor_WithInvalidUsZipCode_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new Address("123 Main St", "Springfield", "IL", "62A01", "US"));
    }

    [Test]
    public void Constructor_WithLongUsZipCode_ShouldCreateAddress()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701-1234", "US");

        Assert.That(address.ZipCode, Is.EqualTo("62701-1234"));
    }

    [Test]
    public void Equality_SameValues_ShouldBeEqual()
    {
        var a = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var b = new Address("123 Main St", "Springfield", "IL", "62701", "US");

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Equality_DifferentValues_ShouldNotBeEqual()
    {
        var a = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        var b = new Address("456 Oak Ave", "Springfield", "IL", "62701", "US");

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void ToString_ShouldReturnFormattedAddress()
    {
        var address = new Address("123 Main St", "Springfield", "IL", "62701", "US");

        var result = address.ToString();

        Assert.That(result, Is.EqualTo("123 Main St, Springfield, IL 62701, US"));
    }
}
