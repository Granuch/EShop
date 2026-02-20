using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Catalog.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Products.EventHandlers;

/// <summary>
/// Handles ProductPriceChangedEvent by enqueuing a ProductPriceChangedIntegrationEvent
/// for cross-service communication via the outbox pattern.
/// </summary>
public class ProductPriceChangedEventHandler : INotificationHandler<ProductPriceChangedEvent>
{
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ProductPriceChangedEventHandler> _logger;

    public ProductPriceChangedEventHandler(
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<ProductPriceChangedEventHandler> logger)
    {
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public Task Handle(ProductPriceChangedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Product price changed: ProductId={ProductId}, OldPrice={OldPrice}, NewPrice={NewPrice}",
            notification.ProductId,
            notification.OldPrice,
            notification.NewPrice);

        _outbox.Enqueue(new ProductPriceChangedIntegrationEvent
        {
            ProductId = notification.ProductId,
            OldPrice = notification.OldPrice,
            NewPrice = notification.NewPrice,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        return Task.CompletedTask;
    }
}
