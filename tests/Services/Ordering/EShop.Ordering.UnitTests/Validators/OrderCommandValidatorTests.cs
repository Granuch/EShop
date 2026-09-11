using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
using EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;
using EShop.Ordering.Application.Orders.Commands.ShipOrder;
using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;
using FluentValidation.TestHelper;

namespace EShop.Ordering.UnitTests.Validators;

[TestFixture]
public class OrderCommandValidatorTests
{
    private CreateOrderCommandValidator _createValidator = null!;
    private CreateCheckedOutOrderCommandValidator _checkedOutValidator = null!;
    private CancelOrderCommandValidator _cancelValidator = null!;
    private ShipOrderCommandValidator _shipValidator = null!;
    private AddOrderItemCommandValidator _addItemValidator = null!;
    private RemoveOrderItemCommandValidator _removeItemValidator = null!;
    private GetOrderByIdQueryValidator _getByIdValidator = null!;
    private GetOrdersByUserQueryValidator _getByUserValidator = null!;

    [SetUp]
    public void SetUp()
    {
        _createValidator = new CreateOrderCommandValidator();
        _checkedOutValidator = new CreateCheckedOutOrderCommandValidator();
        _cancelValidator = new CancelOrderCommandValidator();
        _shipValidator = new ShipOrderCommandValidator();
        _addItemValidator = new AddOrderItemCommandValidator();
        _removeItemValidator = new RemoveOrderItemCommandValidator();
        _getByIdValidator = new GetOrderByIdQueryValidator();
        _getByUserValidator = new GetOrdersByUserQueryValidator();
    }

    #region CreateOrderCommand

    private static CreateOrderCommand ValidCreate() => new()
    {
        UserId = "user-1",
        Street = "123 Main St",
        City = "Springfield",
        State = "IL",
        ZipCode = "62701",
        Country = "US",
        Items = [new() { ProductId = Guid.NewGuid(), Quantity = 1 }]
    };

    [Test]
    public void CreateOrder_ValidCommand_ShouldHaveNoErrors()
    {
        _createValidator.TestValidate(ValidCreate()).ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void CreateOrder_EmptyUserId_ShouldHaveError()
    {
        _createValidator.TestValidate(ValidCreate() with { UserId = "" })
            .ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Test]
    public void CreateOrder_EmptyItems_ShouldHaveError()
    {
        _createValidator.TestValidate(ValidCreate() with { Items = [] })
            .ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Test]
    public void CreateOrder_EmptyStreet_ShouldHaveError()
    {
        _createValidator.TestValidate(ValidCreate() with { Street = "" })
            .ShouldHaveValidationErrorFor(x => x.Street);
    }

    [Test]
    public void CreateOrder_EmptyCity_ShouldHaveError()
    {
        _createValidator.TestValidate(ValidCreate() with { City = "" })
            .ShouldHaveValidationErrorFor(x => x.City);
    }

    [Test]
    public void CreateOrder_EmptyCountry_ShouldHaveError()
    {
        _createValidator.TestValidate(ValidCreate() with { Country = "" })
            .ShouldHaveValidationErrorFor(x => x.Country);
    }

    [Test]
    public void CreateOrder_ItemWithEmptyProductId_ShouldHaveError()
    {
        var result = _createValidator.TestValidate(
            ValidCreate() with { Items = [new() { ProductId = Guid.Empty, Quantity = 1 }] });
        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void CreateOrder_ItemWithZeroQuantity_ShouldHaveError()
    {
        var result = _createValidator.TestValidate(
            ValidCreate() with { Items = [new() { ProductId = Guid.NewGuid(), Quantity = 0 }] });
        Assert.That(result.IsValid, Is.False);
    }

    /// <summary>The domain refuses a second line for the same product; say so before pricing it.</summary>
    [Test]
    public void CreateOrder_SameProductTwice_ShouldHaveError()
    {
        var productId = Guid.NewGuid();
        var command = ValidCreate() with
        {
            Items =
            [
                new() { ProductId = productId, Quantity = 1 },
                new() { ProductId = productId, Quantity = 2 }
            ]
        };

        _createValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Items);
    }

    #endregion

    #region CreateCheckedOutOrderCommand

    private static CreateCheckedOutOrderCommand ValidCheckedOut() => new()
    {
        UserId = "user-1",
        Street = "123 Main St",
        City = "Springfield",
        State = "IL",
        ZipCode = "62701",
        Country = "US",
        Items = [new() { ProductId = Guid.NewGuid(), ProductName = "Widget", UnitPrice = 10.00m, Quantity = 1 }]
    };

    [Test]
    public void CreateCheckedOutOrder_ValidCommand_ShouldHaveNoErrors()
    {
        _checkedOutValidator.TestValidate(ValidCheckedOut()).ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void CreateCheckedOutOrder_ItemWithNegativePrice_ShouldHaveError()
    {
        var command = ValidCheckedOut() with
        {
            Items = [new() { ProductId = Guid.NewGuid(), ProductName = "Widget", UnitPrice = -1.00m, Quantity = 1 }]
        };

        Assert.That(_checkedOutValidator.TestValidate(command).IsValid, Is.False);
    }

    [Test]
    public void CreateCheckedOutOrder_ItemWithEmptyName_ShouldHaveError()
    {
        var command = ValidCheckedOut() with
        {
            Items = [new() { ProductId = Guid.NewGuid(), ProductName = "", UnitPrice = 1.00m, Quantity = 1 }]
        };

        Assert.That(_checkedOutValidator.TestValidate(command).IsValid, Is.False);
    }

    #endregion

    #region CancelOrderCommand

    [Test]
    public void CancelOrder_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new CancelOrderCommand { OrderId = Guid.NewGuid(), Reason = "Changed my mind" };
        var result = _cancelValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void CancelOrder_EmptyOrderId_ShouldHaveError()
    {
        var command = new CancelOrderCommand { OrderId = Guid.Empty, Reason = "Reason" };
        var result = _cancelValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Test]
    public void CancelOrder_EmptyReason_ShouldHaveError()
    {
        var command = new CancelOrderCommand { OrderId = Guid.NewGuid(), Reason = "" };
        var result = _cancelValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Test]
    public void CancelOrder_ReasonExceeds500Characters_ShouldHaveError()
    {
        var command = new CancelOrderCommand { OrderId = Guid.NewGuid(), Reason = new string('x', 501) };
        var result = _cancelValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    #endregion

    #region ShipOrderCommand

    [Test]
    public void ShipOrder_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new ShipOrderCommand { OrderId = Guid.NewGuid() };
        var result = _shipValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void ShipOrder_EmptyOrderId_ShouldHaveError()
    {
        var command = new ShipOrderCommand { OrderId = Guid.Empty };
        var result = _shipValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    #endregion

    #region AddOrderItemCommand

    [Test]
    public void AddOrderItem_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new AddOrderItemCommand { OrderId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 1 };
        _addItemValidator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void AddOrderItem_EmptyOrderId_ShouldHaveError()
    {
        var command = new AddOrderItemCommand { OrderId = Guid.Empty, ProductId = Guid.NewGuid(), Quantity = 1 };
        _addItemValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Test]
    public void AddOrderItem_EmptyProductId_ShouldHaveError()
    {
        var command = new AddOrderItemCommand { OrderId = Guid.NewGuid(), ProductId = Guid.Empty, Quantity = 1 };
        _addItemValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.ProductId);
    }

    [Test]
    public void AddOrderItem_ZeroQuantity_ShouldHaveError()
    {
        var command = new AddOrderItemCommand { OrderId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 0 };
        _addItemValidator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    #endregion

    #region RemoveOrderItemCommand

    [Test]
    public void RemoveOrderItem_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new RemoveOrderItemCommand { OrderId = Guid.NewGuid(), ItemId = Guid.NewGuid() };
        var result = _removeItemValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void RemoveOrderItem_EmptyOrderId_ShouldHaveError()
    {
        var command = new RemoveOrderItemCommand { OrderId = Guid.Empty, ItemId = Guid.NewGuid() };
        var result = _removeItemValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Test]
    public void RemoveOrderItem_EmptyItemId_ShouldHaveError()
    {
        var command = new RemoveOrderItemCommand { OrderId = Guid.NewGuid(), ItemId = Guid.Empty };
        var result = _removeItemValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ItemId);
    }

    #endregion

    #region GetOrderByIdQuery

    [Test]
    public void GetOrderById_ValidQuery_ShouldHaveNoErrors()
    {
        var query = new GetOrderByIdQuery { OrderId = Guid.NewGuid() };
        var result = _getByIdValidator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void GetOrderById_EmptyOrderId_ShouldHaveError()
    {
        var query = new GetOrderByIdQuery { OrderId = Guid.Empty };
        var result = _getByIdValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    #endregion

    #region GetOrdersByUserQuery

    [Test]
    public void GetOrdersByUser_ValidQuery_ShouldHaveNoErrors()
    {
        var query = new GetOrdersByUserQuery { UserId = "user-1" };
        var result = _getByUserValidator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void GetOrdersByUser_EmptyUserId_ShouldHaveError()
    {
        var query = new GetOrdersByUserQuery { UserId = "" };
        var result = _getByUserValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    #endregion
}
