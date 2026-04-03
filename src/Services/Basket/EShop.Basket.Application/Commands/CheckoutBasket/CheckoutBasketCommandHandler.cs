using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Application.Telemetry;
using EShop.Basket.Domain.Events;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Handler for basket checkout
/// </summary>
public class CheckoutBasketCommandHandler : IRequestHandler<CheckoutBasketCommand, Result<Guid>>
{
    private readonly IBasketRepository _basketRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<CheckoutBasketCommandHandler> _logger;
    private readonly IBasketMetrics _metrics;

    public CheckoutBasketCommandHandler(
        IBasketRepository basketRepository,
        IMediator mediator,
        ILogger<CheckoutBasketCommandHandler> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<Guid>> Handle(CheckoutBasketCommand request, CancellationToken cancellationToken)
    {
        using var activity = BasketActivitySource.Source.StartActivity("Basket.Checkout");
        using var timer = _metrics.MeasureOperation("checkout");

        activity?.SetTag("basket.user_id", request.UserId);

        try
        {
            var basket = await _basketRepository.GetBasketAsync(request.UserId, cancellationToken);
            if (basket == null || basket.Items.Count == 0)
            {
                _metrics.RecordCheckout("failure");
                return Result<Guid>.Failure(BasketErrors.BasketEmpty);
            }

            basket.Checkout(request.ShippingAddress, request.PaymentMethod);

            var domainEvent = basket.DomainEvents
                .OfType<BasketCheckedOutDomainEvent>()
                .SingleOrDefault();

            if (domainEvent == null)
            {
                _metrics.RecordCheckout("failure");
                return Result<Guid>.Failure(BasketErrors.BasketOperationFailed);
            }

            await _mediator.Publish(domainEvent, cancellationToken);
            basket.ClearDomainEvents();

            await _basketRepository.DeleteBasketAsync(request.UserId, cancellationToken);

            _metrics.RecordCheckout("success");
            activity?.SetTag("basket.total_price", domainEvent.TotalPrice);

            return Result<Guid>.Success(domainEvent.EventId);
        }
        catch (Exception ex)
        {
            _metrics.RecordCheckout("failure");

            _logger.LogError(ex,
                "Failed to checkout basket. UserId={UserId}",
                request.UserId);

            return Result<Guid>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}
