using MassTransit;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Notification.Application.Consumers;

/// <summary>
/// Consumer for OrderShippedEvent - sends shipping notification email
/// </summary>
public class OrderShippedConsumer : IConsumer<OrderShippedEvent>
{
    // TODO: Inject IEmailService, ILogger
    // private readonly IEmailService _emailService;

    public async Task Consume(ConsumeContext<OrderShippedEvent> context)
    {
        // TODO: Map event to OrderShippedEmail
        // TODO: Call _emailService.SendOrderShippedAsync()
        // TODO: Include tracking number if available
        throw new NotImplementedException();
    }
}
