using EShop.Ordering.Application.Orders.Commands.AddOrderItem;
using EShop.Ordering.Application.Orders.Commands.CancelOrder;
using EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;
using EShop.Ordering.Application.Orders.Commands.ShipOrder;
using EShop.Ordering.Application.Orders.Commands.UpdateOrderItemQuantity;
using EShop.Ordering.Application.Orders.Commands.UpdateShippingAddress;
using EShop.Ordering.Application.Orders.Queries.GetOrderById;
using EShop.Ordering.Application.Orders.Queries.GetOrders;
using EShop.Ordering.Application.Orders.Queries.GetOrderStats;
using EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.ValueObjects;
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

    /// <summary>
    /// Audit M1: the validator and <see cref="Address"/> must agree on every address, in both
    /// directions. Before, only non-emptiness was checked, so "USA" or a blank state passed validation
    /// and threw inside the handler — and nothing compared the two sets of rules.
    /// </summary>
    [TestCase("123 Main St", "Springfield", "IL", "62701", "US")]
    [TestCase("1 Khreshchatyk St", "Kyiv", "Kyiv", "01001", "UA")]
    [TestCase(" 123 Main St ", "Springfield", "IL", " 62701 ", " us ")]
    [TestCase("123 Main St", "Springfield", "IL", "62701", "USA")]
    [TestCase("123 Main St", "Springfield", "", "62701", "US")]
    [TestCase("123 Main St", "Springfield", "IL", "ABCDE", "US")]
    [TestCase("12", "Springfield", "IL", "62701", "US")]
    [TestCase("123 Main St", "Spr1ngfield", "IL", "62701", "US")]
    [TestCase("10 Downing St", "London", "Westminster", "12", "GB")]
    public void CreateOrder_AcceptsExactlyTheAddressesAddressAccepts(
        string street, string city, string state, string zipCode, string country)
    {
        var addressAccepts = Address.Validate(street, city, state, zipCode, country).Count == 0;
        var command = ValidCreate() with
        {
            Street = street, City = city, State = state, ZipCode = zipCode, Country = country
        };
        var checkedOut = ValidCheckedOut() with
        {
            Street = street, City = city, State = state, ZipCode = zipCode, Country = country
        };

        Assert.That(_createValidator.TestValidate(command).IsValid, Is.EqualTo(addressAccepts), "CreateOrder");
        Assert.That(_checkedOutValidator.TestValidate(checkedOut).IsValid, Is.EqualTo(addressAccepts), "CreateCheckedOutOrder");
    }

    /// <summary>Each problem is reported against the field the client sent, not the command as a whole.</summary>
    [Test]
    public void CreateOrder_InvalidAddress_NamesTheOffendingFields()
    {
        // A zip that fails on length: the US format rule applies only once the country is a valid "US".
        var result = _createValidator.TestValidate(ValidCreate() with { Country = "USA", ZipCode = "12" });

        result.ShouldHaveValidationErrorFor(x => x.Country);
        result.ShouldHaveValidationErrorFor(x => x.ZipCode);
        result.ShouldNotHaveValidationErrorFor(x => x.Street);
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

    /// <summary>The column's limit, reported as a failed Result rather than a database error.</summary>
    [Test]
    public void CreateCheckedOutOrder_ItemNameLongerThanTheColumn_ShouldHaveError()
    {
        CreateCheckedOutOrderCommand WithName(int length) => ValidCheckedOut() with
        {
            Items = [new() { ProductId = Guid.NewGuid(), ProductName = new string('x', length), UnitPrice = 1.00m, Quantity = 1 }]
        };

        Assert.That(_checkedOutValidator.TestValidate(WithName(OrderItem.MaxProductNameLength + 1)).IsValid, Is.False);
        Assert.That(_checkedOutValidator.TestValidate(WithName(OrderItem.MaxProductNameLength)).IsValid, Is.True);
    }

    #endregion

    #region GetOrdersQuery

    [Test]
    public void GetOrders_DefaultQuery_ShouldHaveNoErrors()
    {
        new GetOrdersQueryValidator().TestValidate(new GetOrdersQuery()).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>Audit M3: the admin list had no validator at all.</summary>
    [TestCase(0, null, null, nameof(GetOrdersQuery.PageNumber))]
    [TestCase(null, 0, null, nameof(GetOrdersQuery.PageSize))]
    [TestCase(null, 101, null, nameof(GetOrdersQuery.PageSize))]
    [TestCase(null, null, "Payed", nameof(GetOrdersQuery.Status))]
    [TestCase(null, null, "7", nameof(GetOrdersQuery.Status))]
    public void GetOrders_InvalidQuery_NamesTheParameterAsSent(int? page, int? size, string? status, string field)
    {
        var query = new GetOrdersQuery { PageNumber = page, PageSize = size, Status = status };

        new GetOrdersQueryValidator().TestValidate(query).ShouldHaveValidationErrorFor(field);
    }

    [TestCase("Paid")]
    [TestCase("paid")]
    [TestCase("CANCELLED")]
    public void GetOrders_StatusName_IsAcceptedInAnyCase(string status)
    {
        new GetOrdersQueryValidator().TestValidate(new GetOrdersQuery { Status = status })
            .ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Admin panel S8. Every element, not just the first: one typo among several names would
    /// otherwise narrow the list to the ones that parsed, which reads as a working filter.
    /// </summary>
    [Test]
    public void GetOrders_OneBadNameAmongTheStatuses_IsRejected()
    {
        var query = new GetOrdersQuery { Statuses = ["Paid", "Payed", "Shipped"] };

        new GetOrdersQueryValidator().TestValidate(query)
            .ShouldHaveValidationErrorFor(nameof(GetOrdersQuery.Statuses) + "[1]");
    }

    [Test]
    public void GetOrders_SeveralGoodStatusNames_AreAccepted()
    {
        var query = new GetOrdersQuery { Statuses = ["paid", "SHIPPED"], Status = "Delivered" };

        new GetOrdersQueryValidator().TestValidate(query).ShouldNotHaveAnyValidationErrors();
    }

    [TestCase("nonsense", nameof(GetOrdersQuery.SortBy))]
    [TestCase("0", nameof(GetOrdersQuery.SortBy))]
    public void GetOrders_AnUnknownSortColumn_IsRejected(string sortBy, string field)
    {
        new GetOrdersQueryValidator().TestValidate(new GetOrdersQuery { SortBy = sortBy })
            .ShouldHaveValidationErrorFor(field);
    }

    [TestCase("CreatedAt")]
    [TestCase("totalprice")]
    [TestCase("STATUS")]
    public void GetOrders_AKnownSortColumn_IsAcceptedInAnyCase(string sortBy)
    {
        new GetOrdersQueryValidator().TestValidate(new GetOrdersQuery { SortBy = sortBy })
            .ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>An inverted range is a mistake, and answering it with an empty page hides the mistake.</summary>
    [Test]
    public void GetOrders_AnInvertedDateRange_IsRejected()
    {
        var query = new GetOrdersQuery
        {
            From = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        new GetOrdersQueryValidator().TestValidate(query)
            .ShouldHaveValidationErrorFor(nameof(GetOrdersQuery.To));
    }

    [Test]
    public void GetOrders_AnInvertedTotalRange_IsRejected()
    {
        var query = new GetOrdersQuery { MinTotal = 100m, MaxTotal = 10m };

        new GetOrdersQueryValidator().TestValidate(query)
            .ShouldHaveValidationErrorFor(nameof(GetOrdersQuery.MaxTotal));
    }

    [Test]
    public void GetOrders_ANegativeMinimumTotal_IsRejected()
    {
        new GetOrdersQueryValidator().TestValidate(new GetOrdersQuery { MinTotal = -1m })
            .ShouldHaveValidationErrorFor(nameof(GetOrdersQuery.MinTotal));
    }

    [Test]
    public void GetOrders_AnOverlongSearchTerm_IsRejected()
    {
        var query = new GetOrdersQuery { Search = new string('x', GetOrdersQueryValidator.MaxSearchLength + 1) };

        new GetOrdersQueryValidator().TestValidate(query)
            .ShouldHaveValidationErrorFor(nameof(GetOrdersQuery.Search));
    }

    /// <summary>One bound alone is a half-open range, and must not be mistaken for an inverted one.</summary>
    [Test]
    public void GetOrders_ASingleDateBound_IsAccepted()
    {
        new GetOrdersQueryValidator()
            .TestValidate(new GetOrdersQuery { From = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) })
            .ShouldNotHaveAnyValidationErrors();
    }

    #endregion

    #region GetOrderStatsQuery

    [Test]
    public void GetOrderStats_DefaultQuery_ShouldHaveNoErrors()
    {
        new GetOrderStatsQueryValidator().TestValidate(new GetOrderStatsQuery())
            .ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// <c>week</c> is the one a caller is most likely to try, and the one this API deliberately does
    /// not have — so it must be an error, not daily buckets labelled as weeks.
    /// </summary>
    [TestCase("week")]
    [TestCase("hour")]
    [TestCase("1")]
    public void GetOrderStats_AnUnknownGrouping_IsRejected(string groupBy)
    {
        new GetOrderStatsQueryValidator().TestValidate(new GetOrderStatsQuery { GroupBy = groupBy })
            .ShouldHaveValidationErrorFor(nameof(GetOrderStatsQuery.GroupBy));
    }

    [TestCase("Day")]
    [TestCase("month")]
    [TestCase("YEAR")]
    public void GetOrderStats_AKnownGrouping_IsAcceptedInAnyCase(string groupBy)
    {
        new GetOrderStatsQueryValidator().TestValidate(new GetOrderStatsQuery { GroupBy = groupBy })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void GetOrderStats_AnInvertedDateRange_IsRejected()
    {
        var query = new GetOrderStatsQuery
        {
            From = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        new GetOrderStatsQueryValidator().TestValidate(query)
            .ShouldHaveValidationErrorFor(nameof(GetOrderStatsQuery.To));
    }

    #endregion

    #region UpdateOrderItemQuantityCommand

    private static UpdateOrderItemQuantityCommand ValidQuantityChange() => new()
    {
        OrderId = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        Quantity = 3
    };

    [Test]
    public void UpdateItemQuantity_ValidCommand_ShouldHaveNoErrors()
    {
        new UpdateOrderItemQuantityCommandValidator().TestValidate(ValidQuantityChange())
            .ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void UpdateItemQuantity_WithoutAnOrderId_IsRejected()
    {
        var command = ValidQuantityChange() with { OrderId = Guid.Empty };

        new UpdateOrderItemQuantityCommandValidator().TestValidate(command)
            .ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    [Test]
    public void UpdateItemQuantity_WithoutAnItemId_IsRejected()
    {
        var command = ValidQuantityChange() with { ItemId = Guid.Empty };

        new UpdateOrderItemQuantityCommandValidator().TestValidate(command)
            .ShouldHaveValidationErrorFor(x => x.ItemId);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void UpdateItemQuantity_WithANonPositiveQuantity_IsRejected(int quantity)
    {
        var command = ValidQuantityChange() with { Quantity = quantity };

        new UpdateOrderItemQuantityCommandValidator().TestValidate(command)
            .ShouldHaveValidationErrorFor(x => x.Quantity);
    }

    /// <summary>
    /// Deliberately no upper bound, matching add and create: a cap here alone would make a quantity
    /// reachable by adding a line and unreachable by correcting one. <c>Order.MaxTotal</c> is what
    /// actually bounds it.
    /// </summary>
    [Test]
    public void UpdateItemQuantity_WithAVeryLargeQuantity_PassesValidation()
    {
        var command = ValidQuantityChange() with { Quantity = 1_000_000 };

        new UpdateOrderItemQuantityCommandValidator().TestValidate(command)
            .ShouldNotHaveAnyValidationErrors();
    }

    #endregion

    #region UpdateShippingAddressCommand

    private static UpdateShippingAddressCommand ValidAddressChange() => new()
    {
        OrderId = Guid.NewGuid(),
        Street = "742 Evergreen Terrace",
        City = "Springfield",
        State = "IL",
        ZipCode = "62701",
        Country = "US"
    };

    [Test]
    public void UpdateShippingAddress_ValidCommand_ShouldHaveNoErrors()
    {
        new UpdateShippingAddressCommandValidator().TestValidate(ValidAddressChange())
            .ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void UpdateShippingAddress_WithoutAnOrderId_IsRejected()
    {
        var command = ValidAddressChange() with { OrderId = Guid.Empty };

        new UpdateShippingAddressCommandValidator().TestValidate(command)
            .ShouldHaveValidationErrorFor(x => x.OrderId);
    }

    /// <summary>
    /// The rules come from <c>Address.Validate</c> through <c>ShippingAddressRules</c>, so an address
    /// this validator accepts is one the value object accepts — no second copy to drift. Each case
    /// names the field it concerns, which is what makes the 400 usable.
    /// </summary>
    [TestCase("ab", "Springfield", "IL", "62701", "US", "Street")]
    [TestCase("742 Evergreen Terrace", "S", "IL", "62701", "US", "City")]
    [TestCase("742 Evergreen Terrace", "Springfield", "", "62701", "US", "State")]
    [TestCase("742 Evergreen Terrace", "Springfield", "IL", "62701", "USA", "Country")]
    [TestCase("742 Evergreen Terrace", "Springfield", "IL", "abcde", "US", "ZipCode")]
    public void UpdateShippingAddress_AnInvalidAddress_NamesTheField(
        string street, string city, string state, string zip, string country, string field)
    {
        var command = ValidAddressChange() with
        {
            Street = street,
            City = city,
            State = state,
            ZipCode = zip,
            Country = country
        };

        new UpdateShippingAddressCommandValidator().TestValidate(command)
            .ShouldHaveValidationErrorFor(field);
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

    /// <summary>Audit M4: cursor paging was removed, and a cursor is rejected rather than ignored.</summary>
    [TestCase("2026-01-01T00:00:00Z")]
    [TestCase("anything")]
    public void GetOrdersByUser_AnyCursor_ShouldHaveError(string cursor)
    {
        _getByUserValidator.TestValidate(new GetOrdersByUserQuery { UserId = "user-1", Cursor = cursor })
            .ShouldHaveValidationErrorFor(x => x.Cursor);
    }

    [TestCase(0, null, nameof(GetOrdersByUserQuery.PageNumber))]
    [TestCase(null, 101, nameof(GetOrdersByUserQuery.PageSize))]
    public void GetOrdersByUser_InvalidPaging_NamesTheParameterAsSent(int? page, int? size, string field)
    {
        _getByUserValidator.TestValidate(new GetOrdersByUserQuery { UserId = "user-1", PageNumber = page, PageSize = size })
            .ShouldHaveValidationErrorFor(field);
    }

    #endregion
}
