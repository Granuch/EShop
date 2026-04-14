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

            var basket = await _basketRepository.GetBasketAsync(request.UserId, cancellationToken)
                ?? ShoppingBasket.Create(request.UserId);

            basket.AddItem(product.ProductId, product.ProductName, product.Price, request.Quantity);

            await _basketRepository.SaveBasketAsync(basket, cancellationToken);

            _metrics.RecordItemAdded("api");
            return Result<Unit>.Success(Unit.Value);
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
