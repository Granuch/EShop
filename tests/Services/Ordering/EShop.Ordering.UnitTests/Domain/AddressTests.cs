using EShop.BuildingBlocks.Domain.Exceptions;
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

    /// <summary>
    /// Audit M1: DomainException, because that is what the middleware maps to 400. These were
    /// ArgumentExceptions, which it does not map, so every one of them was a 500.
    /// </summary>
    [TestCase("", "Springfield", "IL", "62701", "US", "Street")]
    [TestCase("123 Main St", "Springfield", "IL", "62701", "USA", "Country")]
    [TestCase("123 Main St", "Springfield", "IL", "62A01", "US", "ZipCode")]
    [TestCase("123 Main St", "Springfield", "", "62701", "US", "State")]
    [TestCase("123 Main St", "Spr1ngfield", "IL", "62701", "US", "City")]
    public void Constructor_WithAnInvalidPart_ThrowsDomainException(
        string street, string city, string state, string zipCode, string country, string field)
    {
        Assert.Throws<DomainException>(() => new Address(street, city, state, zipCode, country));
        Assert.That(Address.Validate(street, city, state, zipCode, country).Select(p => p.Field),
            Is.EqualTo(new[] { field }));
    }

    [Test]
    public void Validate_ReportsEveryBrokenRule_NotJustTheFirst()
    {
        var fields = Address.Validate("", "", "", "", "").Select(p => p.Field);

        Assert.That(fields, Is.EquivalentTo(new[] { "Street", "City", "State", "ZipCode", "Country" }));
    }

    [Test]
    public void Validate_AcceptsWhatTheConstructorNormalizes()
    {
        Assert.That(Address.Validate(" 123 Main St ", "Springfield", "IL", " 62701 ", " us "), Is.Empty);

        var address = new Address(" 123 Main St ", "Springfield", "IL", " 62701 ", " us ");
        Assert.That(address.Street, Is.EqualTo("123 Main St"));
        Assert.That(address.ZipCode, Is.EqualTo("62701"));
        Assert.That(address.Country, Is.EqualTo("US"));
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
