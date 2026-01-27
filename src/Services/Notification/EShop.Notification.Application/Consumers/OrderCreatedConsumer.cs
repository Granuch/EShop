using MassTransit;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Notification.Application.Consumers;

/// <summary>
/// Consumer for OrderCreatedEvent - sends order confirmation email
/// </summary>
public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    // TODO: Inject IEmailService, ILogger
    // private readonly IEmailService _emailService;
    // private readonly ILogger<OrderCreatedConsumer> _logger;

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        // TODO: Map event to OrderConfirmationEmail
        // TODO: Call _emailService.SendOrderConfirmationAsync()
        // TODO: Log success/failure
        // TODO: Add retry logic for failed email sends
        throw new NotImplementedException();
    }
}
