using EShop.BuildingBlocks.Application;
using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Application.Orders;

/// <summary>Errors shared by the add-item and remove-item handlers.</summary>
public static class OrderItemErrors
{
    /// <summary>
    /// The items of a paid order are what the customer was charged for, so they cannot change. Mapped
    /// to 409: the request is well-formed, the order is past the point where it applies.
    /// </summary>
    public static Error NotModifiable(OrderStatus status) => new(
        "Order.NotModifiable",
        $"Items can only be changed while the order is pending; this order is {status.ToString().ToLowerInvariant()}.");
}
