using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Application.Telemetry;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain.Exceptions;
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
            return await BasketWrites.RunAsync(async ct =>
            {
                var basket = await _basketRepository.GetBasketAsync(request.UserId, ct);
                if (basket == null)
                {
                    return Result<Unit>.Failure(BasketErrors.BasketNotFound);
                }

                // Basket audit S8 (M4): a product that is not in the basket is a 404. It used to reach the domain's
                // DomainException, which the generic catch below reported as a 400 and logged as an error.
                if (basket.Items.All(item => item.ProductId != request.ProductId))
                {
                    return Result<Unit>.Failure(BasketErrors.ItemNotFound);
                }

                basket.UpdateItemQuantity(request.ProductId, request.Quantity);

                var written = basket.Items.Count == 0
                    ? await _basketRepository.TryDeleteBasketAsync(basket, ct)
                    : await _basketRepository.TrySaveBasketAsync(basket, ct);

                if (!written)
                {
                    return null;
                }

                return BasketWrites.Done;
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex,
                "Invalid basket quantity update. UserId={UserId}, ProductId={ProductId}",
                request.UserId,
                request.ProductId);

            return Result<Unit>.Failure(new Error("Basket.ValidationFailed", ex.Message));
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
