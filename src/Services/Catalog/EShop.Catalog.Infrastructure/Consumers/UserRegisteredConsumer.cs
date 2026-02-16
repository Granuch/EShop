using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Catalog.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Infrastructure.Consumers;

/// <summary>
/// Consumes UserRegisteredIntegrationEvent from the Identity service.
/// Idempotent — duplicate messages are safely ignored.
/// 
/// This consumer can be extended to create customer-specific catalog views
/// or user preference records in the Catalog bounded context.
/// </summary>
public class UserRegisteredConsumer : IdempotentConsumer<UserRegisteredIntegrationEvent, CatalogDbContext>
{
    public UserRegisteredConsumer(
        CatalogDbContext dbContext,
        ILogger<UserRegisteredConsumer> logger)
        : base(dbContext, logger)
    {
    }

    protected override Task HandleAsync(
        ConsumeContext<UserRegisteredIntegrationEvent> context,
        CancellationToken cancellationToken)
    {
        var message = context.Message;

        Logger.LogInformation(
            "Catalog received UserRegistered event. UserId={UserId}",
            message.UserId);

        // Extend here: create user preferences, personalized catalog views, etc.

        return Task.CompletedTask;
    }
}
