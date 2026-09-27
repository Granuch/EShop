using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Orders;
using EShop.Ordering.Application.Orders.Commands.UpdateOrderItemQuantity;
using EShop.Ordering.Application.Orders.Commands.UpdateShippingAddress;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;
using Moq;

namespace EShop.Ordering.UnitTests.Orders;

/// <summary>
/// Admin panel S8. The two new write handlers, whose job is entirely the pre-checks: the aggregate
/// throws a <c>DomainException</c> for every one of these, and the middleware maps that to 400 —
/// which is the wrong answer for a missing sub-resource (404) and for an order past the point where
/// the request applies (409).
/// </summary>
[TestFixture]
public class OrderAdminEditingCommandHandlerTests
{
    private Mock<IOrderRepository> _orderRepositoryMock = null!;
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<ICacheInvalidationContext> _cacheContextMock = null!;
    private UpdateOrderItemQuantityCommandHandler _quantityHandler = null!;
    private UpdateShippingAddressCommandHandler _addressHandler = null!;

    [SetUp]
    public void SetUp()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _cacheContextMock = new Mock<ICacheInvalidationContext>();
        _unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _quantityHandler = new UpdateOrderItemQuantityCommandHandler(
            _orderRepositoryMock.Object, _unitOfWorkMock.Object, _cacheContextMock.Object);
        _addressHandler = new UpdateShippingAddressCommandHandler(
            _orderRepositoryMock.Object, _unitOfWorkMock.Object, _cacheContextMock.Object);
    }

    private static readonly Address SeededAddress = new("123 Test St", "TestCity", "TS", "12345", "US");

    private Order Given(Func<Order, Order>? transition = null)
    {
        var order = Order.Create("user-7", SeededAddress,
        [
            new OrderItem(Guid.NewGuid(), "Widget A", 10.00m, 2),
            new OrderItem(Guid.NewGuid(), "Widget B", 25.50m, 1)
        ]);
        order = transition?.Invoke(order) ?? order;

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        return order;
    }

    private static Order Paid(Order order)
    {
        order.MarkAsPaid("pi_test", order.TotalPrice);
        return order;
    }

    #region UpdateOrderItemQuantity

    [Test]
    public async Task UpdateQuantity_OnAPendingOrder_Succeeds_AndSaves()
    {
        var order = Given();
        var item = order.Items.First();

        var result = await _quantityHandler.Handle(
            new UpdateOrderItemQuantityCommand { OrderId = order.Id, ItemId = item.Id, Quantity = 5 },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(item.Quantity, Is.EqualTo(5));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task UpdateQuantity_InvalidatesTheOwnersListFamily()
    {
        var order = Given();

        await _quantityHandler.Handle(
            new UpdateOrderItemQuantityCommand
            {
                OrderId = order.Id,
                ItemId = order.Items.First().Id,
                Quantity = 4
            },
            CancellationToken.None);

        _cacheContextMock.Verify(x => x.AddFamily(OrderCacheKeys.UserOrders("user-7")), Times.Once);
    }

    [Test]
    public async Task UpdateQuantity_OnAMissingOrder_IsOrderNotFound()
    {
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await _quantityHandler.Handle(
            new UpdateOrderItemQuantityCommand { OrderId = Guid.NewGuid(), ItemId = Guid.NewGuid(), Quantity = 1 },
            CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotFound"));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A missing line must be <c>OrderItem.NotFound</c>, which the endpoint's suffix rule turns into a
    /// 404. Left to the aggregate it would be a DomainException and therefore a 400.
    /// </summary>
    [Test]
    public async Task UpdateQuantity_OnAMissingItem_IsOrderItemNotFound_AndWritesNothing()
    {
        var order = Given();

        var result = await _quantityHandler.Handle(
            new UpdateOrderItemQuantityCommand { OrderId = order.Id, ItemId = Guid.NewGuid(), Quantity = 3 },
            CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("OrderItem.NotFound"));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UpdateQuantity_OnAPaidOrder_IsNotModifiable_AndChangesNothing()
    {
        var order = Given(Paid);
        var item = order.Items.First();

        var result = await _quantityHandler.Handle(
            new UpdateOrderItemQuantityCommand { OrderId = order.Id, ItemId = item.Id, Quantity = 9 },
            CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotModifiable"));
        Assert.That(item.Quantity, Is.EqualTo(2));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The state check must come first. A paid order with an unknown item id has two things wrong with
    /// it, and answering 404 would tell an operator to look for a line that is not the problem.
    /// </summary>
    [Test]
    public async Task UpdateQuantity_OnAPaidOrderWithAnUnknownItem_ReportsTheState_NotTheItem()
    {
        var order = Given(Paid);

        var result = await _quantityHandler.Handle(
            new UpdateOrderItemQuantityCommand { OrderId = order.Id, ItemId = Guid.NewGuid(), Quantity = 2 },
            CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotModifiable"));
    }

    #endregion

    #region UpdateShippingAddress

    private static UpdateShippingAddressCommand AddressChangeFor(Guid orderId) => new()
    {
        OrderId = orderId,
        Street = "9 New Ave",
        City = "Shelbyville",
        State = "IL",
        ZipCode = "62565",
        Country = "US"
    };

    [Test]
    public async Task UpdateAddress_OnAPendingOrder_Succeeds_AndSaves()
    {
        var order = Given();

        var result = await _addressHandler.Handle(AddressChangeFor(order.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(order.ShippingAddress.City, Is.EqualTo("Shelbyville"));
        _orderRepositoryMock.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task UpdateAddress_OnAPaidOrder_IsStillAllowed()
    {
        var order = Given(Paid);

        var result = await _addressHandler.Handle(AddressChangeFor(order.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(order.ShippingAddress.City, Is.EqualTo("Shelbyville"));
    }

    [Test]
    public async Task UpdateAddress_OnAMissingOrder_IsOrderNotFound()
    {
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await _addressHandler.Handle(AddressChangeFor(Guid.NewGuid()), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.NotFound"));
    }

    /// <summary>
    /// A distinct code from <c>Order.NotModifiable</c>: that one says items can only change while
    /// pending, which is a different rule — the address survives payment — and wrong advice here.
    /// </summary>
    [Test]
    public async Task UpdateAddress_OnAShippedOrder_IsAddressNotModifiable_AndChangesNothing()
    {
        var order = Given(o =>
        {
            Paid(o);
            o.Ship();
            return o;
        });

        var result = await _addressHandler.Handle(AddressChangeFor(order.Id), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.AddressNotModifiable"));
        Assert.That(order.ShippingAddress, Is.EqualTo(SeededAddress));
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UpdateAddress_OnACancelledOrder_IsAddressNotModifiable()
    {
        var order = Given(o =>
        {
            o.Cancel("no longer wanted");
            return o;
        });

        var result = await _addressHandler.Handle(AddressChangeFor(order.Id), CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.AddressNotModifiable"));
    }

    [Test]
    public async Task UpdateAddress_InvalidatesTheOwnersListFamily()
    {
        var order = Given();

        await _addressHandler.Handle(AddressChangeFor(order.Id), CancellationToken.None);

        _cacheContextMock.Verify(x => x.AddFamily(OrderCacheKeys.UserOrders("user-7")), Times.Once);
    }

    /// <summary>
    /// The state check runs before the <c>Address</c> is constructed. <c>Address</c> validates in its
    /// constructor and throws, and a throw out of a handler is a different response than the 409 this
    /// owes — and under <c>TransactionBehavior</c> the ordering is the difference between rolling back
    /// and committing a half-applied change.
    /// </summary>
    [Test]
    public async Task UpdateAddress_OnAShippedOrderWithAnInvalidAddress_ReportsTheState_NotTheAddress()
    {
        var order = Given(o =>
        {
            Paid(o);
            o.Ship();
            return o;
        });

        var result = await _addressHandler.Handle(
            AddressChangeFor(order.Id) with { Country = "not-a-country" },
            CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("Order.AddressNotModifiable"));
    }

    #endregion
}
