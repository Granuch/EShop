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
}
