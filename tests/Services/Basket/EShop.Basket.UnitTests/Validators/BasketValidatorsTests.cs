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

    [Test]
    public void Checkout_TooLongFields_ShouldHaveErrors()
    {
        var command = new CheckoutBasketCommand
        {
            UserId = "user-1",
            ShippingAddress = new string('a', 501),
            PaymentMethod = new string('p', 101)
        };

        var result = _checkoutValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ShippingAddress);
        result.ShouldHaveValidationErrorFor(x => x.PaymentMethod);
    }

    [Test]
    public void GetBasket_EmptyUserId_ShouldHaveError()
    {
        var query = new GetBasketQuery { UserId = string.Empty };

        var result = _getBasketValidator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }
}
