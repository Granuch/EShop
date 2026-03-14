using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
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
        _cancelValidator = new CancelOrderCommandValidator();
        _shipValidator = new ShipOrderCommandValidator();
        _addItemValidator = new AddOrderItemCommandValidator();
        _removeItemValidator = new RemoveOrderItemCommandValidator();
        _getByIdValidator = new GetOrderByIdQueryValidator();
        _getByUserValidator = new GetOrdersByUserQueryValidator();
    }

    #region CreateOrderCommand

    [Test]
    public void CreateOrder_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            State = "IL",
            ZipCode = "62701",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void CreateOrder_EmptyUserId_ShouldHaveError()
    {
        var command = new CreateOrderCommand
        {
            UserId = "",
            Street = "123 Main St",
            City = "Springfield",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Test]
    public void CreateOrder_EmptyItems_ShouldHaveError()
    {
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            Country = "US",
            Items = new List<CreateOrderItemDto>()
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Test]
    public void CreateOrder_EmptyStreet_ShouldHaveError()
    {
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "",
            City = "Springfield",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Street);
    }

    [Test]
    public void CreateOrder_EmptyCity_ShouldHaveError()
    {
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.City);
    }

    [Test]
    public void CreateOrder_EmptyCountry_ShouldHaveError()
    {
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            Country = "",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        var result = _createValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Country);
    }

    [Test]
    public void CreateOrder_ItemWithEmptyProductId_ShouldHaveError()
    {
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.Empty, ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        var result = _createValidator.TestValidate(command);
        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void CreateOrder_ItemWithZeroQuantity_ShouldHaveError()
    {
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 0 }
            }
        };

        var result = _createValidator.TestValidate(command);
        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void CreateOrder_ItemWithNegativePrice_ShouldHaveError()
    {
        var command = new CreateOrderCommand
        {
            UserId = "user-1",
            Street = "123 Main St",
            City = "Springfield",
            Country = "US",
            Items = new List<CreateOrderItemDto>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = -1.00m, Quantity = 1 }
            }
        };

        var result = _createValidator.TestValidate(command);
        Assert.That(result.IsValid, Is.False);
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
        var command = new AddOrderItemCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Widget",
            UnitPrice = 10.00m,
            Quantity = 1
        };
        var result = _addItemValidator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void AddOrderItem_EmptyOrderId_ShouldHaveError()
    {
        var command = new AddOrderItemCommand
        {
            OrderId = Guid.Empty,
            ProductId = Guid.NewGuid(),
            ProductName = "Widget",
            UnitPrice = 10.00m,
            Quantity = 1
        };
        var result = _addItemValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Test]
    public void AddOrderItem_EmptyProductName_ShouldHaveError()
    {
        var command = new AddOrderItemCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "",
            UnitPrice = 10.00m,
            Quantity = 1
        };
        var result = _addItemValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ProductName);
    }

    [Test]
    public void AddOrderItem_NegativeUnitPrice_ShouldHaveError()
    {
        var command = new AddOrderItemCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Widget",
            UnitPrice = -5.00m,
            Quantity = 1
        };
        var result = _addItemValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.UnitPrice);
    }

    [Test]
    public void AddOrderItem_ZeroQuantity_ShouldHaveError()
    {
        var command = new AddOrderItemCommand
        {
            OrderId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Widget",
            UnitPrice = 10.00m,
            Quantity = 0
        };
        var result = _addItemValidator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Quantity);
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
