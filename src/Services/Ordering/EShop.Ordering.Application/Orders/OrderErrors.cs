using EShop.BuildingBlocks.Application;
using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Application.Orders;

/// <summary>Errors about the order itself, as opposed to one of its lines (<see cref="OrderItemErrors"/>).</summary>
public static class OrderErrors
{
    /// <summary>
    /// The order has shipped, been delivered, cancelled or refunded, so its shipping address no
    /// longer decides where anything goes. Mapped to 409: the request is well-formed, the order is
    /// past the point where it applies.
    ///
    /// <para>
    /// A code of its own rather than reusing <c>Order.NotModifiable</c>, whose message says items can
    /// only be changed while pending — which is both a different rule (the address survives payment)
    /// and wrong advice here.
    /// </para>
    /// </summary>
    public static Error AddressNotModifiable(OrderStatus status) => new(
        "Order.AddressNotModifiable",
        "The shipping address can only be changed before the order ships; "
        + $"this order is {status.ToString().ToLowerInvariant()}.");

    /// <summary>
    /// The order already carries <see cref="Order.MaxNotes"/> notes (Admin panel S9). Mapped to 400 by
    /// <c>OrderEndpoints.StatusFor</c>'s default arm, matching Catalog's image and attribute caps —
    /// this is a bound on the request, not a state the order will grow out of.
    /// </summary>
    public static readonly Error NoteLimitReached = new(
        "Order.NoteLimitReached",
        $"An order may carry at most {Order.MaxNotes} notes.");
}
