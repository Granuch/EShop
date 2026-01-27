using MassTransit;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Payment.Application.Consumers;

/// <summary>
/// Consumer for OrderCreatedEvent - processes payment
/// </summary>
public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    // TODO: Inject IPaymentProcessor, IPublishEndpoint, ILogger
    // private readonly IPaymentProcessor _paymentProcessor;
    // private readonly IPublishEndpoint _publishEndpoint;
    // private readonly ILogger<OrderCreatedConsumer> _logger;

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        // TODO: Log payment processing start
        // TODO: Call _paymentProcessor.ProcessPaymentAsync() with mock/real implementation
        // TODO: If success, publish PaymentSuccessEvent
        // TODO: If failure, publish PaymentFailedEvent
        // TODO: Add retry logic with Polly
        throw new NotImplementedException();
    }
}
