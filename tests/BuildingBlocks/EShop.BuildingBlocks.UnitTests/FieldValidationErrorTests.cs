using EShop.BuildingBlocks.Application;
using FluentValidation.Results;

namespace EShop.BuildingBlocks.UnitTests;

/// <summary>
/// Frontend-contracts F-03. The one validation shape: keys are the field's camelCase wire name, a rule about the whole
/// request is keyed <c>$</c>, and <c>detail</c> lists the messages without property prefixes.
/// </summary>
[TestFixture]
public class FieldValidationErrorTests
{
    [TestCase("PageSize", "pageSize")]
    [TestCase("Items[0].Quantity", "items[0].quantity")]
    [TestCase("ShippingAddress.ZipCode", "shippingAddress.zipCode")]
    [TestCase("SKU", "sku")]
    [TestCase("email", "email")]
    [TestCase("", "$")]
    [TestCase("$", "$")]
    [TestCase("$.email", "$.email")]
    public void KeyFor_IsTheWireName(string propertyPath, string expected)
        => Assert.That(FieldValidationError.KeyFor(propertyPath), Is.EqualTo(expected));

    [Test]
    public void From_GroupsByWireName_AndDropsRepeatedMessages()
    {
        var error = FieldValidationError.From(
        [
            new ValidationFailure("Name", "Name is required"),
            new ValidationFailure("Name", "Name is required"),
            new ValidationFailure("Name", "Name must be at most 200 characters"),
            new ValidationFailure("", "Supply either delta or quantity"),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo("ValidationError"));
            Assert.That(error.Errors["name"], Is.EqualTo(new[] { "Name is required", "Name must be at most 200 characters" }));
            Assert.That(error.Errors["$"], Is.EqualTo(new[] { "Supply either delta or quantity" }));
            Assert.That(error.Message, Is.EqualTo(
                "Name is required; Name must be at most 200 characters; Supply either delta or quantity"));
        });
    }

    [Test]
    public void For_KeysASingleField()
    {
        var error = FieldValidationError.For("pageSize", "'pageSize' must be between 1 and 100.");

        Assert.That(error.Errors.Keys, Is.EqualTo(new[] { "pageSize" }));
        Assert.That(error.Message, Is.EqualTo("'pageSize' must be between 1 and 100."));
    }
}
