using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Infrastructure.Consumers;

/// <summary>
/// Idempotent consumer for PaymentSuccessEvent.
/// Marks the order as paid.
/// </summary>
public class PaymentSuccessConsumer : IdempotentConsumer<PaymentSuccessEvent, OrderingDbContext>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PaymentSuccessConsumer(
        OrderingDbContext dbContext,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ILogger<PaymentSuccessConsumer> logger)
        : base(dbContext, logger)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    protected override async Task HandleAsync(ConsumeContext<PaymentSuccessEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;

        Logger.LogInformation(
            "Processing PaymentSuccessEvent for OrderId={OrderId}, PaymentIntentId={PaymentIntentId}",
            message.OrderId,
            message.PaymentIntentId);

        var order = await _orderRepository.GetByIdAsync(message.OrderId, cancellationToken);
        if (order is null)
        {
            Logger.LogWarning("Order {OrderId} not found for PaymentSuccessEvent", message.OrderId);
            return;
        }

        if (order.Status == OrderStatus.Paid)
        {
            Logger.LogInformation(
                "Order {OrderId} already marked as paid. Skipping duplicate PaymentSuccessEvent.",
                message.OrderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            Logger.LogWarning(
                "Skipping PaymentSuccessEvent for OrderId={OrderId} because order status is {Status}.",
                message.OrderId,
                order.Status);
            return;
        }

        order.MarkAsPaid(message.PaymentIntentId);
        order.Ship();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Order {OrderId} marked as paid and shipped by payment-success orchestration", message.OrderId);
    }
}
