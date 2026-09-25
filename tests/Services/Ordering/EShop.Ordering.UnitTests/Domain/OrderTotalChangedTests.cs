using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Events;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.UnitTests.Domain;

/// <summary>
/// frontend-contracts F-47. Payment charges the amount it recorded when the order was created, and
/// <see cref="Order.MarkAsPaid"/> refuses a payment that differs from the current total. So every item change that moves
/// the total must tell Payment, and nothing else should.
/// </summary>
[TestFixture]
public class OrderTotalChangedTests
{
    private static readonly Address ValidAddress = new("123 Main St", "Springfield", "IL", "62701", "US");

    /// <summary>A Pending order worth 45.50 (10.00 x 2 + 25.50), with its creation event already dispatched.</summary>
    private static Order PendingOrder()
    {
        var order = Order.Create("user-1", ValidAddress,
        [
            new OrderItem(Guid.NewGuid(), "Widget A", 10.00m, 2),
            new OrderItem(Guid.NewGuid(), "Widget B", 25.50m, 1)
        ]);
        order.ClearDomainEvents();
        return order;
    }

    private static OrderTotalChangedDomainEvent[] TotalChanges(Order order)
        => order.DomainEvents.OfType<OrderTotalChangedDomainEvent>().ToArray();

    [Test]
    public void Create_RaisesNoTotalChange_PaymentLearnsTheTotalFromOrderCreated()
    {
        var order = Order.Create("user-1", ValidAddress, [new OrderItem(Guid.NewGuid(), "Widget A", 10.00m, 1)]);

        Assert.That(TotalChanges(order), Is.Empty);
    }

    [Test]
    public void AddingAnItem_RaisesTheNewWholeTotal()
    {
        var order = PendingOrder();

        order.AddItem(Guid.NewGuid(), "Widget C", 4.50m, 2);

        var change = TotalChanges(order).Single();
        Assert.Multiple(() =>
        {
            Assert.That(change.OrderId, Is.EqualTo(order.Id));
            Assert.That(change.UserId, Is.EqualTo("user-1"));
            Assert.That(change.NewTotal, Is.EqualTo(54.50m), "the whole total, not the 9.00 delta");
            Assert.That(change.NewTotal, Is.EqualTo(order.TotalPrice));
        });
    }

    [Test]
    public void RemovingAnItem_RaisesTheNewTotal()
    {
        var order = PendingOrder();
        var widgetB = order.Items.Single(i => i.ProductName == "Widget B");

        order.RemoveItem(widgetB.Id);

        Assert.That(TotalChanges(order).Single().NewTotal, Is.EqualTo(20.00m));
    }

    [Test]
    public void ChangingAQuantity_RaisesTheNewTotal()
    {
        var order = PendingOrder();
        var widgetA = order.Items.Single(i => i.ProductName == "Widget A");

        order.UpdateItemQuantity(widgetA.Id, 5);

        Assert.That(TotalChanges(order).Single().NewTotal, Is.EqualTo(75.50m));
    }

    [Test]
    public void SettingTheQuantityItAlreadyHas_RaisesNothing()
    {
        var order = PendingOrder();
        var widgetA = order.Items.Single(i => i.ProductName == "Widget A");

        order.UpdateItemQuantity(widgetA.Id, 2);

        Assert.That(order.DomainEvents, Is.Empty);
    }

    [Test]
    public void EachChange_RaisesItsOwnEvent_InOrder()
    {
        var order = PendingOrder();
        var widgetA = order.Items.Single(i => i.ProductName == "Widget A");

        order.UpdateItemQuantity(widgetA.Id, 3);
        order.AddItem(Guid.NewGuid(), "Widget C", 1.00m, 1);

        Assert.That(TotalChanges(order).Select(e => e.NewTotal), Is.EqualTo(new[] { 55.50m, 56.50m }));
    }

    [Test]
    public void ARefusedChange_RaisesNothing()
    {
        var order = PendingOrder();
        var widgetA = order.Items.Single(i => i.ProductName == "Widget A");

        Assert.Throws<DomainException>(() => order.AddItem(widgetA.ProductId, "Widget A", 10.00m, 1));
        Assert.Throws<DomainException>(() => order.UpdateItemQuantity(Guid.NewGuid(), 3));

        Assert.That(order.DomainEvents, Is.Empty);
    }
}
