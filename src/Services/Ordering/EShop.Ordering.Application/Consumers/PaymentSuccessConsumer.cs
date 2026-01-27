using MassTransit;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Ordering.Application.Consumers;

/// <summary>
/// Consumer for PaymentSuccessEvent
/// </summary>
public class PaymentSuccessConsumer : IConsumer<PaymentSuccessEvent>
{
    // TODO: Inject IOrderRepository, ILogger
    // private readonly IOrderRepository _orderRepository;

    public async Task Consume(ConsumeContext<PaymentSuccessEvent> context)
    {
        // TODO: Find order by OrderId
        // TODO: Call order.MarkAsPaid(paymentIntentId)
        // TODO: Save changes
        // TODO: Publish OrderPaidEvent for notification service
        throw new NotImplementedException();
    }
}
