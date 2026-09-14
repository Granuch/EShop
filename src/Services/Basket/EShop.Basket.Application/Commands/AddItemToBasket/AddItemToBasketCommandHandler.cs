using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Basket.Application.Abstractions;
using EShop.Basket.Application.Common;
using EShop.Basket.Application.Telemetry;
using EShop.BuildingBlocks.Domain.Exceptions;
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
    private readonly IProductCatalogReader _productCatalogReader;
    private readonly ILogger<AddItemToBasketCommandHandler> _logger;
    private readonly IBasketMetrics _metrics;

    public AddItemToBasketCommandHandler(
        IBasketRepository basketRepository,
        IProductCatalogReader productCatalogReader,
        ILogger<AddItemToBasketCommandHandler> logger,
        IBasketMetrics metrics)
    {
        _basketRepository = basketRepository;
        _productCatalogReader = productCatalogReader;
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
            var product = await _productCatalogReader.GetByIdAsync(request.ProductId, cancellationToken);
            if (product is null)
            {
                _logger.LogWarning(
                    "Cannot add product to basket because product was not found in catalog. UserId={UserId}, ProductId={ProductId}",
                    request.UserId,
                    request.ProductId);

                return Result<Unit>.Failure(BasketErrors.ProductNotFound);
            }

            // Catalog is read once; only the basket is re-read if the conditional save loses a race (S4).
            var result = await BasketWrites.RunAsync(async ct =>
            {
                var basket = await _basketRepository.GetBasketAsync(request.UserId, ct)
                    ?? ShoppingBasket.Create(request.UserId);

                basket.AddItem(product.ProductId, product.ProductName, product.Price, request.Quantity);

                if (!await _basketRepository.TrySaveBasketAsync(basket, ct))
                {
                    return null;
                }

                return BasketWrites.Done;
            }, cancellationToken);

            if (result.IsSuccess)
            {
                _metrics.RecordItemAdded("api");
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex,
                "Invalid basket operation while adding item. UserId={UserId}, ProductId={ProductId}",
                request.UserId,
                request.ProductId);

            return Result<Unit>.Failure(new Error("Basket.ValidationFailed", ex.Message));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Catalog lookup failed while adding item to basket. UserId={UserId}, ProductId={ProductId}",
                request.UserId,
                request.ProductId);

            return Result<Unit>.Failure(BasketErrors.ProductVerificationFailed);
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
