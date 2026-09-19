using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Application.Orders.Commands.UpdateShippingAddress;

/// <summary>
/// Replaces an order's shipping address before it ships (Admin panel S8, endpoint #59).
///
/// <para>
/// Full replacement, not a patch: an address is a value object, and a partial update would have to
/// answer "what does an omitted State mean on an address that has one" — three cases per field, five
/// fields. Every field is required, exactly as on create, and <c>Address.Validate</c> is the one
/// statement of what a storable address is.
/// </para>
/// </summary>
public record UpdateShippingAddressCommand : IRequest<Result>, ITransactionalCommand, ICacheInvalidatingCommand
{
    public Guid OrderId { get; init; }

    // A home address is personal data; LoggingBehavior logs every command at Information (audit L7).
    // Country is not marked, matching CreateOrderCommand: a two-letter ISO code identifies nobody.
    [SensitiveData] public string Street { get; init; } = string.Empty;
    [SensitiveData] public string City { get; init; } = string.Empty;
    [SensitiveData] public string State { get; init; } = string.Empty;
    [SensitiveData] public string ZipCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;

    /// <summary>The user's list family is added by the handler, which is where the user id is known.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [OrderCacheKeys.Order(OrderId)];
}
