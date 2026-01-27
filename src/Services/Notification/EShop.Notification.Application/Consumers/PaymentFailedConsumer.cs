using MassTransit;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Notification.Application.Consumers;

/// <summary>
/// Consumer for PaymentFailedEvent - sends payment failure notification
/// </summary>
public class PaymentFailedConsumer : IConsumer<PaymentFailedEvent>
{
    // TODO: Inject IEmailService, ILogger
    // private readonly IEmailService _emailService;

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        // TODO: Map event to PaymentFailedEmail
        // TODO: Call _emailService.SendPaymentFailedAsync()
        // TODO: Include failure reason
        throw new NotImplementedException();
    }
}
