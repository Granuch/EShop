using EShop.Basket.Application.Commands.AddItemToBasket;
using EShop.Basket.Application.Commands.CheckoutBasket;
using EShop.Basket.Application.Commands.ClearBasket;
using EShop.Basket.Application.Commands.RemoveBasketItem;
using EShop.Basket.Application.Commands.UpdateBasketItemQuantity;
using EShop.Basket.Application.Queries.GetBasket;
using FluentValidation.TestHelper;

namespace EShop.Basket.UnitTests.Validators;

[TestFixture]
public class BasketValidatorsTests
{
    private AddItemToBasketCommandValidator _addItemValidator = null!;
    private UpdateBasketItemQuantityCommandValidator _updateQuantityValidator = null!;
    private RemoveBasketItemCommandValidator _removeItemValidator = null!;
    private ClearBasketCommandValidator _clearBasketValidator = null!;
    private CheckoutBasketCommandValidator _checkoutValidator = null!;
    private GetBasketQueryValidator _getBasketValidator = null!;

    [SetUp]
    public void SetUp()
    {
        _addItemValidator = new AddItemToBasketCommandValidator();
        _updateQuantityValidator = new UpdateBasketItemQuantityCommandValidator();
        _removeItemValidator = new RemoveBasketItemCommandValidator();
        _clearBasketValidator = new ClearBasketCommandValidator();
        _checkoutValidator = new CheckoutBasketCommandValidator();
        _getBasketValidator = new GetBasketQueryValidator();
    }

    [Test]
    public void AddItem_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new AddItemToBasketCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = 2
        };

        var result = _addItemValidator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void AddItem_InvalidFields_ShouldHaveErrors()
    {
        var command = new AddItemToBasketCommand
        {
            UserId = string.Empty,
            ProductId = Guid.Empty,
            Quantity = 0
        };

        var result = _addItemValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
        result.ShouldHaveValidationErrorFor(x => x.ProductId);
        result.ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Test]
    public void UpdateQuantity_NegativeQuantity_ShouldHaveError()
    {
        var command = new UpdateBasketItemQuantityCommand
        {
            UserId = "user-1",
            ProductId = Guid.NewGuid(),
            Quantity = -1
        };

        var result = _updateQuantityValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    [Test]
    public void RemoveItem_EmptyUserId_ShouldHaveError()
    {
        var command = new RemoveBasketItemCommand
        {
            UserId = string.Empty,
            ProductId = Guid.NewGuid()
        };

        var result = _removeItemValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Test]
    public void ClearBasket_EmptyUserId_ShouldHaveError()
    {
        var command = new ClearBasketCommand { UserId = string.Empty };

        var result = _clearBasketValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    private static CheckoutBasketCommand ValidCheckout(CheckoutAddress? address = null) => new()
    {
        UserId = "user-1",
        ShippingAddress = address ?? new CheckoutAddress
        {
            Street = "1 Main St",
            City = "Springfield",
            State = "IL",
            ZipCode = "62701",
            Country = "US"
        },
        PaymentMethod = "Card"
    };

    [Test]
    public void Checkout_ValidStructuredAddress_ShouldHaveNoErrors()
    {
        _checkoutValidator.TestValidate(ValidCheckout()).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A non-US address with a non-numeric postcode is fine — only US zips have a format.</summary>
    [Test]
    public void Checkout_NonUsAddress_ShouldHaveNoErrors()
    {
        var address = new CheckoutAddress
        {
            Street = "10 Downing Street",
            City = "London",
            State = "Greater London",
            ZipCode = "SW1A 2AA",
            Country = "gb"
        };

        _checkoutValidator.TestValidate(ValidCheckout(address)).ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void Checkout_MissingAddress_ShouldHaveError()
    {
        _checkoutValidator.TestValidate(ValidCheckout() with { ShippingAddress = null })
            .ShouldHaveValidationErrorFor(x => x.ShippingAddress);
    }

    /// <summary>
    /// Each case is an address Ordering's Address value object refuses. Basket clears the basket at
    /// checkout, before Ordering reads the event, so anything accepted here and refused there is a
    /// lost order (Ordering audit C2). These cases are the contract between the two.
    /// </summary>
    [TestCase("1 Main St", "Springfield", "IL", "62701", "USA", TestName = "Country must be ISO alpha-2")]
    [TestCase("1 Main St", "Springfield", "IL", "6270", "US", TestName = "US zip must be 5 digits")]
    [TestCase("1 Main St", "Springfield", "", "62701", "US", TestName = "State is required")]
    [TestCase("1 Main St", "Springfield", "IL", "12", "GB", TestName = "Zip must be at least 3 characters")]
    [TestCase("1", "Springfield", "IL", "62701", "US", TestName = "Street must be at least 3 characters")]
    [TestCase("1 Main St", "Spring3field", "IL", "62701", "US", TestName = "City has no digits")]
    public void Checkout_AddressOrderingWouldRefuse_ShouldHaveErrors(
        string street, string city, string state, string zipCode, string country)
    {
        var address = new CheckoutAddress
        {
            Street = street,
            City = city,
            State = state,
            ZipCode = zipCode,
            Country = country
        };

        Assert.That(_checkoutValidator.TestValidate(ValidCheckout(address)).IsValid, Is.False);
    }

    [Test]
    public void Checkout_TooLongPaymentMethod_ShouldHaveError()
    {
        _checkoutValidator.TestValidate(ValidCheckout() with { PaymentMethod = new string('p', 101) })
            .ShouldHaveValidationErrorFor(x => x.PaymentMethod);
    }

    [Test]
    public void GetBasket_EmptyUserId_ShouldHaveError()
    {
        var query = new GetBasketQuery { UserId = string.Empty };

        var result = _getBasketValidator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }
}
