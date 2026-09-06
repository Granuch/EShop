using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Application.Telemetry;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Commands.AddItemToBasket;

/// <summary>
/// Handler for adding item to basket
/// </summary>
public class AddItemToBasketCommandHandler : IRequestHandler<AddItemToBasketCommand, Result<Unit>>
{
    private readonly IBasketRepository _basketRepository;
    private readonly ILogger<AddItemToBasketCommandHandler> _logger;
    private readonly IBasketMetrics _metrics;

    public AddItemToBasketCommandHandler(
        IBasketRepository basketRepository,
        ILogger<AddItemToBasketCommandHandler> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<Unit>> Handle(AddItemToBasketCommand request, CancellationToken cancellationToken)
    {
        using var activity = BasketActivitySource.Source.StartActivity("Basket.AddItem");
        using var timer = _metrics.MeasureOperation("add_item");

        activity?.SetTag("basket.user_id", request.UserId);
        activity?.SetTag("basket.product_id", request.ProductId.ToString());
        activity?.SetTag("basket.quantity", request.Quantity);

        try
        {
            var basket = await _basketRepository.GetBasketAsync(request.UserId, cancellationToken)
                ?? ShoppingBasket.Create(request.UserId);

            basket.AddItem(request.ProductId, request.ProductName, request.Price, request.Quantity);

            await _basketRepository.SaveBasketAsync(basket, cancellationToken);

            _metrics.RecordItemAdded("api");
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to add item to basket. UserId={UserId}, ProductId={ProductId}",
                request.UserId,
                request.ProductId);

            return Result<Unit>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}
