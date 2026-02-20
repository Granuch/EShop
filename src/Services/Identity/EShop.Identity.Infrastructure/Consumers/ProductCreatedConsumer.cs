using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Infrastructure.Consumers;

/// <summary>
/// Consumes ProductCreatedIntegrationEvent from the Catalog service.
/// Idempotent — duplicate messages are safely ignored.
/// 
/// This consumer can be extended to send notifications to users
/// about new products or update user activity feeds.
/// </summary>
public class ProductCreatedConsumer : IdempotentConsumer<ProductCreatedIntegrationEvent, IdentityDbContext>
{
    public ProductCreatedConsumer(
        IdentityDbContext dbContext,
        ILogger<ProductCreatedConsumer> logger)
        : base(dbContext, logger)
    {
    }

    protected override Task HandleAsync(
        ConsumeContext<ProductCreatedIntegrationEvent> context,
        CancellationToken cancellationToken)
    {
        var message = context.Message;

        Logger.LogInformation(
            "Identity received ProductCreated event. ProductId={ProductId}",
            message.ProductId);

        // Extend here: send notification to users, update activity feed, etc.

        return Task.CompletedTask;
    }
}
