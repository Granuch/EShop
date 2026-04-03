using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Application.Orders.Commands.CreateOrder;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Infrastructure.Consumers;

/// <summary>
/// Idempotent consumer for BasketCheckedOutEvent.
/// Creates an order from the checked-out basket.
/// </summary>
public class BasketCheckedOutConsumer : IdempotentConsumer<BasketCheckedOutEvent, OrderingDbContext>
{
    private readonly IMediator _mediator;

    public BasketCheckedOutConsumer(
        OrderingDbContext dbContext,
        IMediator mediator,
        ILogger<BasketCheckedOutConsumer> logger)
        : base(dbContext, logger)
    {
        _mediator = mediator;
    }

    protected override async Task HandleAsync(ConsumeContext<BasketCheckedOutEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;

        Logger.LogInformation(
            "Processing BasketCheckedOutEvent for UserId={UserId}, TotalPrice={TotalPrice}",
            message.UserId,
            message.TotalPrice);

        // Prefer structured shipping address; fallback to legacy string format for backward compatibility.
        var addressParts = message.ShippingAddressDetails is not null
            ? (
                Street: message.ShippingAddressDetails.Street,
                City: message.ShippingAddressDetails.City,
                State: message.ShippingAddressDetails.State,
                ZipCode: message.ShippingAddressDetails.ZipCode,
                Country: message.ShippingAddressDetails.Country
            )
            : ParseAddress(message.ShippingAddress);

        var command = new CreateOrderCommand
        {
            UserId = message.UserId,
            Street = addressParts.Street,
            City = addressParts.City,
            State = addressParts.State,
            ZipCode = addressParts.ZipCode,
            Country = addressParts.Country,
            Items = message.Items.Select(i => new CreateOrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Price = i.Price,
                Quantity = i.Quantity
            }).ToList()
        };

        var result = await _mediator.Send(command, cancellationToken);

        result.Switch(
            orderId => Logger.LogInformation(
                "Order {OrderId} created from BasketCheckedOutEvent for UserId={UserId}",
                orderId, message.UserId),
            error => Logger.LogError(
                "Failed to create order from BasketCheckedOutEvent for UserId={UserId}: {Error}",
                message.UserId, error.Message));
    }

    private static (string Street, string City, string State, string ZipCode, string Country) ParseAddress(string address)
    {
        var parts = address.Split(',', StringSplitOptions.TrimEntries);
        return (
            Street: parts.Length > 0 ? parts[0] : "Unknown",
            City: parts.Length > 1 ? parts[1] : "Unknown",
            State: parts.Length > 2 ? parts[2] : "Unknown",
            ZipCode: parts.Length > 3 ? parts[3] : "00000",
            Country: parts.Length > 4 ? parts[4] : "Unknown"
        );
    }
}
