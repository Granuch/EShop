using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Events;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.UnitTests.Domain;

[TestFixture]
public class OrderTests
{
    private Address _validAddress = null!;
    private List<OrderItem> _validItems = null!;

    [SetUp]
    public void SetUp()
    {
        _validAddress = new Address("123 Main St", "Springfield", "IL", "62701", "US");
        _validItems = new List<OrderItem>
        {
            new(Guid.NewGuid(), "Widget A", 10.00m, 2),
            new(Guid.NewGuid(), "Widget B", 25.50m, 1)
        };
    }

    #region Create

    [Test]
    public void Create_WithValidParameters_ShouldCreateOrder()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        Assert.That(order, Is.Not.Null);
        Assert.That(order.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(order.UserId, Is.EqualTo("user-1"));
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Pending));
        Assert.That(order.Items, Has.Count.EqualTo(2));
        Assert.That(order.TotalPrice, Is.EqualTo(45.50m)); // 10*2 + 25.50*1
        Assert.That(order.ShippingAddress, Is.EqualTo(_validAddress));
    }

    [Test]
    public void Create_WithValidParameters_ShouldRaiseOrderCreatedEvent()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        Assert.That(order.DomainEvents, Has.Count.EqualTo(1));
        var domainEvent = order.DomainEvents[0] as OrderCreatedDomainEvent;
        Assert.That(domainEvent, Is.Not.Null);
        Assert.That(domainEvent!.OrderId, Is.EqualTo(order.Id));
        Assert.That(domainEvent.UserId, Is.EqualTo("user-1"));
        Assert.That(domainEvent.TotalAmount, Is.EqualTo(45.50m));
    }

    [Test]
    public void Create_WithEmptyUserId_ShouldThrowDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Order.Create("", _validAddress, _validItems));
        Assert.That(ex!.Message, Does.Contain("User ID"));
    }

    [Test]
    public void Create_WithNullAddress_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Order.Create("user-1", null!, _validItems));
    }

    [Test]
    public void Create_WithEmptyItems_ShouldThrowDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Order.Create("user-1", _validAddress, new List<OrderItem>()));
        Assert.That(ex!.Message, Does.Contain("at least one item"));
    }

    [Test]
    public void Create_WithNullItems_ShouldThrowDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Order.Create("user-1", _validAddress, null!));
    }

    #endregion

    #region AddItem

    [Test]
    public void AddItem_ToPendingOrder_ShouldAddAndRecalculate()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);
        var newProductId = Guid.NewGuid();

        order.AddItem(newProductId, "Widget C", 5.00m, 3);

        Assert.That(order.Items, Has.Count.EqualTo(3));
        Assert.That(order.TotalPrice, Is.EqualTo(60.50m)); // 45.50 + 5*3
    }

    [Test]
    public void AddItem_DuplicateProduct_ShouldThrowDomainException()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);
        var existingProductId = _validItems[0].ProductId;

        var ex = Assert.Throws<DomainException>(() =>
            order.AddItem(existingProductId, "Widget A", 10.00m, 1));
        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    [Test]
    public void AddItem_WithZeroQuantity_ShouldThrowDomainException()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        var ex = Assert.Throws<DomainException>(() =>
            order.AddItem(Guid.NewGuid(), "Widget C", 5.00m, 0));
        Assert.That(ex!.Message, Does.Contain("Quantity"));
    }

    [Test]
    public void AddItem_WithNegativePrice_ShouldThrowDomainException()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        var ex = Assert.Throws<DomainException>(() =>
            order.AddItem(Guid.NewGuid(), "Widget C", -1.00m, 1));
        Assert.That(ex!.Message, Does.Contain("price"));
    }

    [Test]
    public void AddItem_ToShippedOrder_ShouldThrowDomainException()
    {
        var order = CreatePaidOrder();
        order.Ship();

        Assert.Throws<DomainException>(() =>
            order.AddItem(Guid.NewGuid(), "Widget C", 5.00m, 1));
    }

    #endregion

    #region RemoveItem

    [Test]
    public void RemoveItem_FromOrderWithMultipleItems_ShouldRemoveAndRecalculate()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);
        var itemToRemove = order.Items.First();

        order.RemoveItem(itemToRemove.Id);

        Assert.That(order.Items, Has.Count.EqualTo(1));
        Assert.That(order.TotalPrice, Is.EqualTo(25.50m)); // only Widget B left
    }

    [Test]
    public void RemoveItem_LastItem_ShouldThrowDomainException()
    {
        var singleItem = new List<OrderItem> { new(Guid.NewGuid(), "Widget A", 10.00m, 1) };
        var order = Order.Create("user-1", _validAddress, singleItem);
        var itemId = order.Items.First().Id;

        var ex = Assert.Throws<DomainException>(() =>
            order.RemoveItem(itemId));
        Assert.That(ex!.Message, Does.Contain("at least one item"));
    }

    [Test]
    public void RemoveItem_NonExistentItem_ShouldThrowDomainException()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        Assert.Throws<DomainException>(() =>
            order.RemoveItem(Guid.NewGuid()));
    }

    #endregion

    #region MarkAsPaid

    [Test]
    public void MarkAsPaid_PendingOrder_ShouldSetPaidStatus()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        order.MarkAsPaid("pi_123456");

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Paid));
        Assert.That(order.PaymentIntentId, Is.EqualTo("pi_123456"));
        Assert.That(order.PaidAt, Is.Not.Null);
    }

    [Test]
    public void MarkAsPaid_ShouldRaiseOrderPaidDomainEvent()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);
        order.ClearDomainEvents();

        order.MarkAsPaid("pi_123456");

        Assert.That(order.DomainEvents, Has.Count.EqualTo(1));
        var domainEvent = order.DomainEvents[0] as OrderPaidDomainEvent;
        Assert.That(domainEvent, Is.Not.Null);
        Assert.That(domainEvent!.OrderId, Is.EqualTo(order.Id));
        Assert.That(domainEvent.PaymentIntentId, Is.EqualTo("pi_123456"));
    }

    [Test]
    public void MarkAsPaid_AlreadyPaidOrder_ShouldThrowDomainException()
    {
        var order = CreatePaidOrder();

        Assert.Throws<DomainException>(() =>
            order.MarkAsPaid("pi_another"));
    }

    [Test]
    public void MarkAsPaid_WithEmptyPaymentIntentId_ShouldThrowDomainException()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        var ex = Assert.Throws<DomainException>(() =>
            order.MarkAsPaid(""));
        Assert.That(ex!.Message, Does.Contain("Payment intent"));
    }

    #endregion

    #region Ship

    [Test]
    public void Ship_PaidOrder_ShouldSetShippedStatus()
    {
        var order = CreatePaidOrder();

        order.Ship();

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Shipped));
        Assert.That(order.ShippedAt, Is.Not.Null);
    }

    [Test]
    public void Ship_ShouldRaiseOrderShippedDomainEvent()
    {
        var order = CreatePaidOrder();
        order.ClearDomainEvents();

        order.Ship();

        Assert.That(order.DomainEvents, Has.Count.EqualTo(1));
        var domainEvent = order.DomainEvents[0] as OrderShippedDomainEvent;
        Assert.That(domainEvent, Is.Not.Null);
        Assert.That(domainEvent!.OrderId, Is.EqualTo(order.Id));
    }

    [Test]
    public void Ship_PendingOrder_ShouldThrowDomainException()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        var ex = Assert.Throws<DomainException>(() => order.Ship());
        Assert.That(ex!.Message, Does.Contain("paid"));
    }

    #endregion

    #region Deliver

    [Test]
    public void Deliver_ShippedOrder_ShouldSetDeliveredStatus()
    {
        var order = CreateShippedOrder();

        order.Deliver();

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Delivered));
        Assert.That(order.DeliveredAt, Is.Not.Null);
    }

    [Test]
    public void Deliver_PaidOrder_ShouldThrowDomainException()
    {
        var order = CreatePaidOrder();

        Assert.Throws<DomainException>(() => order.Deliver());
    }

    #endregion

    #region Cancel

    [Test]
    public void Cancel_PendingOrder_ShouldSetCancelledStatus()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        order.Cancel("Changed my mind");

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(order.CancelledAt, Is.Not.Null);
        Assert.That(order.CancellationReason, Is.EqualTo("Changed my mind"));
    }

    [Test]
    public void Cancel_ShouldRaiseOrderCancelledDomainEvent()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);
        order.ClearDomainEvents();

        order.Cancel("Changed my mind");

        Assert.That(order.DomainEvents, Has.Count.EqualTo(1));
        var domainEvent = order.DomainEvents[0] as OrderCancelledDomainEvent;
        Assert.That(domainEvent, Is.Not.Null);
        Assert.That(domainEvent!.OrderId, Is.EqualTo(order.Id));
        Assert.That(domainEvent.Reason, Is.EqualTo("Changed my mind"));
    }

    [Test]
    public void Cancel_PaidOrder_ShouldSucceed()
    {
        var order = CreatePaidOrder();

        order.Cancel("Refund requested");

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
    }

    [Test]
    public void Cancel_ShippedOrder_ShouldThrowDomainException()
    {
        var order = CreateShippedOrder();

        var ex = Assert.Throws<DomainException>(() =>
            order.Cancel("Too late"));
        Assert.That(ex!.Message, Does.Contain("shipped"));
    }

    [Test]
    public void Cancel_DeliveredOrder_ShouldThrowDomainException()
    {
        var order = CreateDeliveredOrder();

        Assert.Throws<DomainException>(() =>
            order.Cancel("Too late"));
    }

    [Test]
    public void Cancel_AlreadyCancelledOrder_ShouldThrowDomainException()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);
        order.Cancel("First cancellation");

        Assert.Throws<DomainException>(() =>
            order.Cancel("Second cancellation"));
    }

    [Test]
    public void Cancel_WithEmptyReason_ShouldThrowDomainException()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);

        var ex = Assert.Throws<DomainException>(() =>
            order.Cancel(""));
        Assert.That(ex!.Message, Does.Contain("reason"));
    }

    #endregion

    #region ClearDomainEvents

    [Test]
    public void ClearDomainEvents_ShouldRemoveAllEvents()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);
        Assert.That(order.DomainEvents, Has.Count.GreaterThan(0));

        order.ClearDomainEvents();

        Assert.That(order.DomainEvents, Has.Count.EqualTo(0));
    }

    #endregion

    #region Helpers

    private Order CreatePaidOrder()
    {
        var order = Order.Create("user-1", _validAddress, _validItems);
        order.MarkAsPaid("pi_123456");
        return order;
    }

    private Order CreateShippedOrder()
    {
        var order = CreatePaidOrder();
        order.Ship();
        return order;
    }

    private Order CreateDeliveredOrder()
    {
        var order = CreateShippedOrder();
        order.Deliver();
        return order;
    }

    #endregion
}
