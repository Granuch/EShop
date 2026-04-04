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
    private static readonly TimeSpan CheckoutProcessingTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CheckoutCompletedTtl = TimeSpan.FromMinutes(15);

    private readonly IBasketRepository _basketRepository;
    private readonly ICheckoutIdempotencyStore _checkoutIdempotencyStore;
    private readonly IMediator _mediator;
    private readonly ILogger<CheckoutBasketCommandHandler> _logger;
    private readonly IBasketMetrics _metrics;

    public CheckoutBasketCommandHandler(
        IBasketRepository basketRepository,
        ICheckoutIdempotencyStore checkoutIdempotencyStore,
        IMediator mediator,
        ILogger<CheckoutBasketCommandHandler> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _checkoutIdempotencyStore = checkoutIdempotencyStore;
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<Guid>> Handle(CheckoutBasketCommand request, CancellationToken cancellationToken)
    {
        using var activity = BasketActivitySource.Source.StartActivity("Basket.Checkout");
        using var timer = _metrics.MeasureOperation("checkout");
        var processingLockAcquired = false;

        activity?.SetTag("basket.user_id", request.UserId);

        try
        {
            var completedCheckoutId = await _checkoutIdempotencyStore
                .GetCompletedCheckoutIdAsync(request.UserId, cancellationToken);

            if (completedCheckoutId.HasValue)
            {
                _logger.LogInformation(
                    "Returning previously completed checkout result. UserId={UserId}, CheckoutId={CheckoutId}",
                    request.UserId,
                    completedCheckoutId.Value);

                _metrics.RecordCheckout("deduplicated");
                return Result<Guid>.Success(completedCheckoutId.Value);
            }

            var lockAcquired = await _checkoutIdempotencyStore
                .TryBeginProcessingAsync(request.UserId, CheckoutProcessingTtl, cancellationToken);

            if (!lockAcquired)
            {
                completedCheckoutId = await _checkoutIdempotencyStore
                    .GetCompletedCheckoutIdAsync(request.UserId, cancellationToken);

                if (completedCheckoutId.HasValue)
                {
                    _metrics.RecordCheckout("deduplicated");
                    return Result<Guid>.Success(completedCheckoutId.Value);
                }

                _metrics.RecordCheckout("in_progress");
                return Result<Guid>.Failure(BasketErrors.CheckoutAlreadyInProgress);
            }

            processingLockAcquired = true;

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

            await _checkoutIdempotencyStore
                .MarkCompletedAsync(request.UserId, domainEvent.EventId, CheckoutCompletedTtl, cancellationToken);

            try
            {
                await _basketRepository.DeleteBasketAsync(request.UserId, cancellationToken);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx,
                    "Basket cleanup failed after successful checkout. UserId={UserId}, CheckoutId={CheckoutId}",
                    request.UserId,
                    domainEvent.EventId);
            }

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
        finally
        {
            if (processingLockAcquired)
            {
                await _checkoutIdempotencyStore.ReleaseProcessingAsync(request.UserId, cancellationToken);
            }
        }
    }
}
