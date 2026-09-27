using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Events;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.UnitTests.Domain;

/// <summary>
/// Admin panel S8: <c>Order.UpdateItemQuantity</c> and <c>Order.UpdateShippingAddress</c>, the two
/// domain methods behind endpoints #57 and #59. Nothing could change either before this stage.
/// </summary>
[TestFixture]
public class OrderAdminEditingTests
{
    private Address _address = null!;

    [SetUp]
    public void SetUp() => _address = new Address("123 Main St", "Springfield", "IL", "62701", "US");

    private Order PendingOrder() => Order.Create("user-1", _address,
    [
        new OrderItem(Guid.NewGuid(), "Widget A", 10.00m, 2),
        new OrderItem(Guid.NewGuid(), "Widget B", 25.50m, 1)
    ]);

    private Order PaidOrder()
    {
        var order = PendingOrder();
        order.MarkAsPaid("pi_test", order.TotalPrice);
        return order;
    }

    #region UpdateItemQuantity

    [Test]
    public void UpdateItemQuantity_ChangesTheLine_AndTheTotal()
    {
        var order = PendingOrder();
        var item = order.Items.First(i => i.ProductName == "Widget A");

        order.UpdateItemQuantity(item.Id, 5);

        Assert.That(item.Quantity, Is.EqualTo(5));
        Assert.That(item.SubTotal, Is.EqualTo(50.00m));
        Assert.That(order.TotalPrice, Is.EqualTo(75.50m)); // 10*5 + 25.50
    }

    /// <summary>The whole reason this exists rather than remove-then-add: the line keeps its identity.</summary>
    [Test]
    public void UpdateItemQuantity_KeepsTheLinesId_AndItsPriceSnapshot()
    {
        var order = PendingOrder();
        var item = order.Items.First();
        var id = item.Id;
        var unitPrice = item.UnitPrice;

        order.UpdateItemQuantity(id, 7);

        Assert.That(order.Items.Select(i => i.Id), Does.Contain(id));
        Assert.That(order.Items.Single(i => i.Id == id).UnitPrice, Is.EqualTo(unitPrice));
        Assert.That(order.Items, Has.Count.EqualTo(2), "no line is added or removed");
    }

    /// <summary>Remove-then-add cannot express this at all: RemoveItem refuses to empty the order.</summary>
    [Test]
    public void UpdateItemQuantity_WorksOnTheOnlyLineOfASingleLineOrder()
    {
        var order = Order.Create("user-1", _address, [new OrderItem(Guid.NewGuid(), "Only", 4.00m, 1)]);
        var item = order.Items.Single();

        order.UpdateItemQuantity(item.Id, 3);

        Assert.That(order.TotalPrice, Is.EqualTo(12.00m));
    }

    [Test]
    public void UpdateItemQuantity_ToTheSameQuantity_IsANoOp()
    {
        var order = PendingOrder();
        var item = order.Items.First();
        var total = order.TotalPrice;

        Assert.DoesNotThrow(() => order.UpdateItemQuantity(item.Id, item.Quantity));
        Assert.That(order.TotalPrice, Is.EqualTo(total));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void UpdateItemQuantity_WithANonPositiveQuantity_Throws(int quantity)
    {
        var order = PendingOrder();
        var item = order.Items.First();

        Assert.Throws<DomainException>(() => order.UpdateItemQuantity(item.Id, quantity));
        Assert.That(item.Quantity, Is.EqualTo(2), "a refused change leaves the line as it was");
    }

    [Test]
    public void UpdateItemQuantity_WithAnUnknownItem_Throws()
    {
        var order = PendingOrder();

        Assert.Throws<DomainException>(() => order.UpdateItemQuantity(Guid.NewGuid(), 3));
    }

    /// <summary>
    /// Guid.Empty is the value a caller reaches by omitting the id, and it must not accidentally
    /// match a line — <c>FirstOrDefault</c> over ids cannot express "not found" at all.
    /// </summary>
    [Test]
    public void UpdateItemQuantity_WithAnEmptyGuid_Throws()
    {
        var order = PendingOrder();

        Assert.Throws<DomainException>(() => order.UpdateItemQuantity(Guid.Empty, 3));
    }

    [Test]
    public void UpdateItemQuantity_OnAPaidOrder_Throws_AndChangesNothing()
    {
        var order = PaidOrder();
        var item = order.Items.First();
        var total = order.TotalPrice;

        Assert.Throws<DomainException>(() => order.UpdateItemQuantity(item.Id, 9));
        Assert.That(item.Quantity, Is.EqualTo(2));
        Assert.That(order.TotalPrice, Is.EqualTo(total));
    }

    /// <summary>
    /// The storability check runs on the total the change would produce, before the change — so an
    /// overflowing quantity leaves the line and the total exactly as they were, rather than writing a
    /// total the numeric(18,2) column refuses with a 22003 that nothing maps.
    /// </summary>
    [Test]
    public void UpdateItemQuantity_PastTheStorableTotal_Throws_AndLeavesTheOrderIntact()
    {
        var order = Order.Create("user-1", _address,
            [new OrderItem(Guid.NewGuid(), "Expensive", 1_000_000_000_000m, 1)]);
        var item = order.Items.Single();

        Assert.Throws<DomainException>(() => order.UpdateItemQuantity(item.Id, 100_000));
        Assert.That(item.Quantity, Is.EqualTo(1));
        Assert.That(order.TotalPrice, Is.EqualTo(1_000_000_000_000m));
    }

    /// <summary>
    /// The check must subtract the line's OLD subtotal, not add on top of it: raising a line from 1 to
    /// 2 in an order already near the ceiling is legal, and treating the new subtotal as an addition
    /// would refuse it.
    /// </summary>
    [Test]
    public void UpdateItemQuantity_DoesNotCountTheLineTwice()
    {
        // 4e15 doubles to 8e15, comfortably under MaxTotal (≈1e16) — but a check that ADDED the new
        // subtotal to the existing total would see 1.2e16 and refuse a legal edit.
        var order = Order.Create("user-1", _address,
            [new OrderItem(Guid.NewGuid(), "Dear", 4_000_000_000_000_000m, 1)]);
        var item = order.Items.Single();

        Assert.DoesNotThrow(() => order.UpdateItemQuantity(item.Id, 2));
        Assert.That(order.TotalPrice, Is.EqualTo(8_000_000_000_000_000m));
    }

    /// <summary>
    /// frontend-contracts F-47. This test used to assert that no event was raised at all, which is exactly why Payment
    /// kept charging the old total. Payment's <c>OrderTotalChangedConsumer</c> now consumes the one it raises.
    /// </summary>
    [Test]
    public void UpdateItemQuantity_RaisesOnlyTheTotalChange()
    {
        var order = PendingOrder();
        order.ClearDomainEvents();

        order.UpdateItemQuantity(order.Items.First().Id, 4);

        Assert.That(order.DomainEvents, Has.Count.EqualTo(1));
        Assert.That(order.DomainEvents[0], Is.InstanceOf<OrderTotalChangedDomainEvent>());
    }

    #endregion

    #region UpdateShippingAddress

    [Test]
    public void UpdateShippingAddress_OnAPendingOrder_ReplacesIt()
    {
        var order = PendingOrder();
        var moved = new Address("9 New Ave", "Shelbyville", "IL", "62565", "US");

        order.UpdateShippingAddress(moved);

        Assert.That(order.ShippingAddress, Is.EqualTo(moved));
    }

    /// <summary>
    /// Paid is deliberately still editable: the money is settled but nothing has left the warehouse,
    /// which is exactly when a customer notices the address is wrong.
    /// </summary>
    [Test]
    public void UpdateShippingAddress_OnAPaidOrder_IsAllowed()
    {
        var order = PaidOrder();
        var moved = new Address("9 New Ave", "Shelbyville", "IL", "62565", "US");

        order.UpdateShippingAddress(moved);

        Assert.That(order.ShippingAddress, Is.EqualTo(moved));
    }

    [Test]
    public void UpdateShippingAddress_OnAShippedOrder_Throws_AndKeepsTheOldAddress()
    {
        var order = PaidOrder();
        order.Ship();

        Assert.Throws<DomainException>(() => order.UpdateShippingAddress(
            new Address("9 New Ave", "Shelbyville", "IL", "62565", "US")));
        Assert.That(order.ShippingAddress, Is.EqualTo(_address));
    }

    [Test]
    public void UpdateShippingAddress_OnADeliveredOrder_Throws()
    {
        var order = PaidOrder();
        order.Ship();
        order.Deliver();

        Assert.Throws<DomainException>(() => order.UpdateShippingAddress(
            new Address("9 New Ave", "Shelbyville", "IL", "62565", "US")));
    }

    [Test]
    public void UpdateShippingAddress_OnACancelledOrder_Throws()
    {
        var order = PendingOrder();
        order.Cancel("changed my mind");

        Assert.Throws<DomainException>(() => order.UpdateShippingAddress(
            new Address("9 New Ave", "Shelbyville", "IL", "62565", "US")));
    }

    [Test]
    public void UpdateShippingAddress_OnARefundedOrder_Throws()
    {
        var order = PaidOrder();
        order.Refund();

        Assert.Throws<DomainException>(() => order.UpdateShippingAddress(
            new Address("9 New Ave", "Shelbyville", "IL", "62565", "US")));
    }

    [Test]
    public void UpdateShippingAddress_WithNull_Throws()
    {
        var order = PendingOrder();

        Assert.Throws<ArgumentNullException>(() => order.UpdateShippingAddress(null!));
    }

    [Test]
    public void UpdateShippingAddress_ChangesNeitherTheTotalNorTheStatus()
    {
        var order = PaidOrder();
        var total = order.TotalPrice;

        order.UpdateShippingAddress(new Address("9 New Ave", "Shelbyville", "IL", "62565", "US"));

        Assert.That(order.TotalPrice, Is.EqualTo(total));
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Paid));
    }

    [Test]
    public void UpdateShippingAddress_RaisesNoDomainEvent()
    {
        var order = PendingOrder();
        order.ClearDomainEvents();

        order.UpdateShippingAddress(new Address("9 New Ave", "Shelbyville", "IL", "62565", "US"));

        Assert.That(order.DomainEvents, Is.Empty);
    }

    #endregion
}
