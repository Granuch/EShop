using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Infrastructure.Consumers;

/// <summary>
/// Idempotent consumer for PaymentFailedEvent.
/// Cancels the order when payment fails.
/// </summary>
public class PaymentFailedConsumer : IdempotentConsumer<PaymentFailedEvent, OrderingDbContext>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PaymentFailedConsumer(
        OrderingDbContext dbContext,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ILogger<PaymentFailedConsumer> logger)
        : base(dbContext, logger)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    protected override async Task HandleAsync(ConsumeContext<PaymentFailedEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;

        Logger.LogInformation(
            "Processing PaymentFailedEvent for OrderId={OrderId}, Reason={Reason}",
            message.OrderId,
            message.Reason);

        var order = await _orderRepository.GetByIdAsync(message.OrderId, cancellationToken);
        if (order is null)
        {
            Logger.LogWarning("Order {OrderId} not found for PaymentFailedEvent", message.OrderId);
            return;
        }

        order.Cancel($"Payment failed: {message.Reason}");

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Order {OrderId} cancelled due to payment failure", message.OrderId);
    }
}
