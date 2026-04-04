using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Application.Telemetry;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Commands.ClearBasket;

public class ClearBasketCommandHandler : IRequestHandler<ClearBasketCommand, Result<Unit>>
{
    private readonly IBasketRepository _basketRepository;
    private readonly ILogger<ClearBasketCommandHandler> _logger;
    private readonly IBasketMetrics _metrics;

    public ClearBasketCommandHandler(
        IBasketRepository basketRepository,
        ILogger<ClearBasketCommandHandler> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<Unit>> Handle(ClearBasketCommand request, CancellationToken cancellationToken)
    {
        using var activity = BasketActivitySource.Source.StartActivity("Basket.Clear");
        using var timer = _metrics.MeasureOperation("clear_basket");

        activity?.SetTag("basket.user_id", request.UserId);

        try
        {
            await _basketRepository.DeleteBasketAsync(request.UserId, cancellationToken);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear basket. UserId={UserId}", request.UserId);
            return Result<Unit>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}
