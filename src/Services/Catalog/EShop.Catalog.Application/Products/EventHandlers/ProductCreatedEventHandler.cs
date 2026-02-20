using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Catalog.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Products.EventHandlers;

/// <summary>
/// Handles ProductCreatedEvent by enqueuing a ProductCreatedIntegrationEvent
/// for cross-service communication via the outbox pattern.
/// </summary>
public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    private readonly IIntegrationEventOutbox _outbox;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<ProductCreatedEventHandler> _logger;

    public ProductCreatedEventHandler(
        IIntegrationEventOutbox outbox,
        ICurrentUserContext currentUserContext,
        ILogger<ProductCreatedEventHandler> logger)
    {
        _outbox = outbox;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public Task Handle(ProductCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Product created: ProductId={ProductId}, Name={ProductName}, Price={Price}",
            notification.ProductId,
            notification.ProductName,
            notification.Price);

        _outbox.Enqueue(new ProductCreatedIntegrationEvent
        {
            ProductId = notification.ProductId,
            ProductName = notification.ProductName,
            Price = notification.Price,
            CorrelationId = _currentUserContext.CorrelationId
        }, _currentUserContext.CorrelationId);

        return Task.CompletedTask;
    }
}
