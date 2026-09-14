using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Application.Telemetry;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Commands.RemoveBasketItem;

public class RemoveBasketItemCommandHandler : IRequestHandler<RemoveBasketItemCommand, Result<Unit>>
{
    private readonly IBasketRepository _basketRepository;
    private readonly ILogger<RemoveBasketItemCommandHandler> _logger;
    private readonly IBasketMetrics _metrics;

    public RemoveBasketItemCommandHandler(
        IBasketRepository basketRepository,
        ILogger<RemoveBasketItemCommandHandler> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<Unit>> Handle(RemoveBasketItemCommand request, CancellationToken cancellationToken)
    {
        using var activity = BasketActivitySource.Source.StartActivity("Basket.RemoveItem");
        using var timer = _metrics.MeasureOperation("remove_item");

        activity?.SetTag("basket.user_id", request.UserId);
        activity?.SetTag("basket.product_id", request.ProductId.ToString());

        try
        {
            return await BasketWrites.RunAsync(async ct =>
            {
                var basket = await _basketRepository.GetBasketAsync(request.UserId, ct);
                if (basket == null)
                {
                    return Result<Unit>.Failure(BasketErrors.BasketNotFound);
                }

                basket.RemoveItem(request.ProductId);

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
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to remove basket item. UserId={UserId}, ProductId={ProductId}",
                request.UserId,
                request.ProductId);

            return Result<Unit>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}
