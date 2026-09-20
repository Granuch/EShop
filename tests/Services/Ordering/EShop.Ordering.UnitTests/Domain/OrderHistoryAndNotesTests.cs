using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.UnitTests.Domain;

/// <summary>
/// Admin panel S9: the two append-only children of <see cref="Order"/>, endpoints #60–#62.
///
/// <para>
/// The history assertions are all made on the aggregate rather than on a database, because that is
/// where the contract lives: the rows are appended <b>by the transition itself</b>, so they exist
/// exactly when it does. The integration fixture proves the other half — that they reach the database
/// in the same <c>SaveChanges</c> and that a refused transition leaves none behind.
/// </para>
/// </summary>
[TestFixture]
public class OrderHistoryAndNotesTests
{
    private Address _address = null!;

    [SetUp]
    public void SetUp() => _address = new Address("123 Main St", "Springfield", "IL", "62701", "US");

    private Order PendingOrder() => Order.Create("user-1", _address,
    [
        new OrderItem(Guid.NewGuid(), "Widget A", 10.00m, 2)
    ]);

    private Order PaidOrder()
    {
        var order = PendingOrder();
        order.MarkAsPaid("pi_test", order.TotalPrice);
        return order;
    }

    private Order ShippedOrder()
    {
        var order = PaidOrder();
        order.Ship();
        return order;
    }

    #region Status history

    [Test]
    public void Create_RecordsTheOpeningRow_WithNoPreviousStatus()
    {
        var order = PendingOrder();

        Assert.That(order.StatusHistory, Has.Count.EqualTo(1));
        var row = order.StatusHistory.Single();
        Assert.That(row.FromStatus, Is.Null, "an order is created Pending; there is no state before it");
        Assert.That(row.ToStatus, Is.EqualTo(OrderStatus.Pending));
        Assert.That(row.OrderId, Is.EqualTo(order.Id));
        Assert.That(row.Reason, Is.Null);
    }

    [Test]
    public void MarkAsPaid_RecordsPendingToPaid()
    {
        var order = PaidOrder();

        var row = order.StatusHistory.Last();
        Assert.That(row.FromStatus, Is.EqualTo(OrderStatus.Pending));
        Assert.That(row.ToStatus, Is.EqualTo(OrderStatus.Paid));
    }

    [Test]
    public void Ship_RecordsPaidToShipped()
    {
        var order = ShippedOrder();

        var row = order.StatusHistory.Last();
        Assert.That(row.FromStatus, Is.EqualTo(OrderStatus.Paid));
        Assert.That(row.ToStatus, Is.EqualTo(OrderStatus.Shipped));
    }

    /// <summary>
    /// <see cref="Order.Deliver"/> raises no domain event at all, which is why a history fed from
    /// domain events could never have been complete.
    /// </summary>
    [Test]
    public void Deliver_RecordsShippedToDelivered_EvenThoughItRaisesNoDomainEvent()
    {
        var order = ShippedOrder();
        order.ClearDomainEvents();

        order.Deliver();

        Assert.That(order.DomainEvents, Is.Empty);
        var row = order.StatusHistory.Last();
        Assert.That(row.FromStatus, Is.EqualTo(OrderStatus.Shipped));
        Assert.That(row.ToStatus, Is.EqualTo(OrderStatus.Delivered));
    }

    /// <inheritdoc cref="Deliver_RecordsShippedToDelivered_EvenThoughItRaisesNoDomainEvent"/>
    [Test]
    public void Refund_RecordsTheStatusItCameFrom_EvenThoughItRaisesNoDomainEvent()
    {
        var order = ShippedOrder();
        order.ClearDomainEvents();

        order.Refund();

        Assert.That(order.DomainEvents, Is.Empty);
        var row = order.StatusHistory.Last();
        Assert.That(row.FromStatus, Is.EqualTo(OrderStatus.Shipped));
        Assert.That(row.ToStatus, Is.EqualTo(OrderStatus.Refunded));
    }

    [Test]
    public void Cancel_RecordsTheReason()
    {
        var order = PendingOrder();

        order.Cancel("  customer changed their mind  ");

        var row = order.StatusHistory.Last();
        Assert.That(row.FromStatus, Is.EqualTo(OrderStatus.Pending));
        Assert.That(row.ToStatus, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(row.Reason, Is.EqualTo("customer changed their mind"), "trimmed, like every other stored string");
    }

    [Test]
    public void TheHistory_IsTheWholeChain_InOrder()
    {
        var order = PendingOrder();
        order.MarkAsPaid("pi_test", order.TotalPrice);
        order.Ship();
        order.Deliver();

        Assert.That(
            order.StatusHistory.Select(h => (h.FromStatus, h.ToStatus)),
            Is.EqualTo(new (OrderStatus?, OrderStatus)[]
            {
                (null, OrderStatus.Pending),
                (OrderStatus.Pending, OrderStatus.Paid),
                (OrderStatus.Paid, OrderStatus.Shipped),
                (OrderStatus.Shipped, OrderStatus.Delivered)
            }));
    }

    /// <summary>
    /// The point of appending inside the transition: a refused one leaves nothing behind, with no
    /// compensating delete anywhere.
    /// </summary>
    [Test]
    public void ARefusedTransition_RecordsNothing()
    {
        var order = PendingOrder();
        var before = order.StatusHistory.Count;

        Assert.Throws<DomainException>(() => order.Ship(), "a pending order cannot ship");

        Assert.That(order.StatusHistory, Has.Count.EqualTo(before));
    }

    [Test]
    public void ARefusedTransition_OnAnOrderThatAlreadyMoved_RecordsNothing()
    {
        var order = PendingOrder();
        order.Cancel("changed mind");
        var before = order.StatusHistory.Count;

        Assert.Throws<DomainException>(() => order.Cancel("again"));

        Assert.That(order.StatusHistory, Has.Count.EqualTo(before));
    }

    /// <summary>
    /// A payment whose amount does not match is refused <b>after</b> the status guard passes, so this
    /// is the case where a history row appended before the check would survive a rejected transition.
    /// </summary>
    [Test]
    public void AMismatchedPayment_RecordsNothing_AndLeavesTheOrderPending()
    {
        var order = PendingOrder();

        Assert.Throws<DomainException>(() => order.MarkAsPaid("pi_test", order.TotalPrice + 1m));

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Pending));
        Assert.That(order.StatusHistory, Has.Count.EqualTo(1));
    }

    [Test]
    public void EachRow_CarriesTheInstantOfItsOwnTransition()
    {
        var order = PendingOrder();
        var created = order.StatusHistory.Single().OccurredAt;

        order.Cancel("changed mind");

        Assert.That(order.StatusHistory.Last().OccurredAt, Is.GreaterThanOrEqualTo(created));
        Assert.That(order.StatusHistory.Last().OccurredAt.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    /// <summary>Editing an order is not a status change, so it writes no history row.</summary>
    [Test]
    public void AnEditThatIsNotATransition_RecordsNothing()
    {
        var order = PendingOrder();
        var before = order.StatusHistory.Count;

        order.UpdateItemQuantity(order.Items.First().Id, 9);
        order.UpdateShippingAddress(new Address("9 New Ave", "Shelbyville", "IL", "62565", "US"));

        Assert.That(order.StatusHistory, Has.Count.EqualTo(before));
    }

    #endregion

    #region Notes

    [Test]
    public void AddNote_StoresTheAuthorTheBodyAndTheOrder()
    {
        var order = PendingOrder();

        var id = order.AddNote("admin-1", "Admin One", "  called the customer  ");

        var note = order.Notes.Single();
        Assert.That(note.Id, Is.EqualTo(id), "the returned id is the note's, so the caller need not re-read");
        Assert.That(note.OrderId, Is.EqualTo(order.Id));
        Assert.That(note.AuthorId, Is.EqualTo("admin-1"));
        Assert.That(note.AuthorName, Is.EqualTo("Admin One"));
        Assert.That(note.Body, Is.EqualTo("called the customer"));
    }

    [Test]
    public void AddNote_GivesEachNoteItsOwnId()
    {
        var order = PendingOrder();

        var first = order.AddNote("admin-1", "Admin One", "first");
        var second = order.AddNote("admin-1", "Admin One", "second");

        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(order.Notes.Select(n => n.Id), Is.Unique);
    }

    /// <summary>
    /// Most of what is worth recording about an order — a chargeback, a complaint, a goodwill credit —
    /// happens after it is final.
    /// </summary>
    [TestCase("pending")]
    [TestCase("paid")]
    [TestCase("shipped")]
    [TestCase("cancelled")]
    public void AddNote_IsAllowedInEveryStatus(string status)
    {
        var order = status switch
        {
            "paid" => PaidOrder(),
            "shipped" => ShippedOrder(),
            "cancelled" => CancelledOrder(),
            _ => PendingOrder()
        };

        Assert.DoesNotThrow(() => order.AddNote("admin-1", "Admin One", "still worth writing down"));
        Assert.That(order.Notes, Has.Count.EqualTo(1));

        Order CancelledOrder()
        {
            var o = PendingOrder();
            o.Cancel("changed mind");
            return o;
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AddNote_WithABlankBody_Throws(string body)
    {
        var order = PendingOrder();

        Assert.Throws<DomainException>(() => order.AddNote("admin-1", "Admin One", body));
        Assert.That(order.Notes, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AddNote_WithNoAuthor_Throws(string authorId)
    {
        var order = PendingOrder();

        Assert.Throws<DomainException>(() => order.AddNote(authorId, "Admin One", "body"));
        Assert.That(order.Notes, Is.Empty, "an unsigned note is worth nothing as a record");
    }

    [Test]
    public void AddNote_WithNoAuthorName_Throws()
    {
        var order = PendingOrder();

        Assert.Throws<DomainException>(() => order.AddNote("admin-1", "  ", "body"));
    }

    [Test]
    public void AddNote_LongerThanTheColumn_Throws()
    {
        var order = PendingOrder();

        Assert.Throws<DomainException>(
            () => order.AddNote("admin-1", "Admin One", new string('x', OrderNote.MaxBodyLength + 1)));
        Assert.That(order.Notes, Is.Empty);
    }

    [Test]
    public void AddNote_AtExactlyTheLimit_IsAccepted()
    {
        var order = PendingOrder();

        Assert.DoesNotThrow(
            () => order.AddNote("admin-1", "Admin One", new string('x', OrderNote.MaxBodyLength)));
    }

    /// <summary>
    /// The one field truncated rather than refused, because it comes from the caller's claims rather
    /// than from the request — see <see cref="OrderNote"/>.
    /// </summary>
    [Test]
    public void AddNote_WithAnOverlongAuthorName_TruncatesItRatherThanRefusingTheNote()
    {
        var order = PendingOrder();

        order.AddNote("admin-1", new string('n', OrderNote.MaxAuthorNameLength + 50), "body");

        Assert.That(order.Notes.Single().AuthorName, Has.Length.EqualTo(OrderNote.MaxAuthorNameLength));
    }

    [Test]
    public void AddNote_RaisesNoDomainEvent()
    {
        var order = PendingOrder();
        order.ClearDomainEvents();

        order.AddNote("admin-1", "Admin One", "body");

        Assert.That(order.DomainEvents, Is.Empty, "nothing consumes one; do not add an event without a consumer");
    }

    [Test]
    public void AddNote_WritesNoHistoryRow()
    {
        var order = PendingOrder();
        var before = order.StatusHistory.Count;

        order.AddNote("admin-1", "Admin One", "body");

        Assert.That(order.StatusHistory, Has.Count.EqualTo(before), "a note is not a state transition");
    }

    #endregion
}
