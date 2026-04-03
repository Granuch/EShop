using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Application.Telemetry;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Commands.UpdateBasketItemQuantity;

public class UpdateBasketItemQuantityCommandHandler : IRequestHandler<UpdateBasketItemQuantityCommand, Result<Unit>>
{
    private readonly IBasketRepository _basketRepository;
    private readonly ILogger<UpdateBasketItemQuantityCommandHandler> _logger;
    private readonly IBasketMetrics _metrics;

    public UpdateBasketItemQuantityCommandHandler(
        IBasketRepository basketRepository,
        ILogger<UpdateBasketItemQuantityCommandHandler> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<Unit>> Handle(UpdateBasketItemQuantityCommand request, CancellationToken cancellationToken)
    {
        using var activity = BasketActivitySource.Source.StartActivity("Basket.UpdateItemQuantity");
        using var timer = _metrics.MeasureOperation("update_item_quantity");

        activity?.SetTag("basket.user_id", request.UserId);
        activity?.SetTag("basket.product_id", request.ProductId.ToString());
        activity?.SetTag("basket.quantity", request.Quantity);

        try
        {
            var basket = await _basketRepository.GetBasketAsync(request.UserId, cancellationToken);
            if (basket == null)
            {
                return Result<Unit>.Failure(BasketErrors.BasketNotFound);
            }

            basket.UpdateItemQuantity(request.ProductId, request.Quantity);

            if (basket.Items.Count == 0)
            {
                await _basketRepository.DeleteBasketAsync(request.UserId, cancellationToken);
            }
            else
            {
                await _basketRepository.SaveBasketAsync(basket, cancellationToken);
            }

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update basket item quantity. UserId={UserId}, ProductId={ProductId}",
                request.UserId,
                request.ProductId);

            return Result<Unit>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}
