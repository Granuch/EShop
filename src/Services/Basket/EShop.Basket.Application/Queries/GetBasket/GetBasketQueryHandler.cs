using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Queries.GetBasket;

public sealed class GetBasketQueryHandler : IRequestHandler<GetBasketQuery, Result<BasketDto>>
{
    private readonly IBasketRepository _basketRepository;
    private readonly ILogger<GetBasketQueryHandler> _logger;

    public GetBasketQueryHandler(IBasketRepository basketRepository, ILogger<GetBasketQueryHandler> logger)
    {
        _basketRepository = basketRepository;
        _logger = logger;
    }

    public async Task<Result<BasketDto>> Handle(GetBasketQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var basket = await _basketRepository.GetBasketAsync(request.UserId, cancellationToken);
            if (basket == null)
            {
                // Basket audit S8 (D8): no basket yet is an empty basket, not a 404 a client has to special-case.
                return Result<BasketDto>.Success(new BasketDto { UserId = request.UserId });
            }

            return Result<BasketDto>.Success(new BasketDto
            {
                UserId = basket.UserId,
                Items = basket.Items.Select(item => new BasketItemDto
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Price = item.Price,
                    Quantity = item.Quantity,
                    SubTotal = item.SubTotal,
                    MainImage = item.MainImageUrl
                }).ToList(),
                TotalPrice = basket.TotalPrice,
                TotalItems = basket.TotalItems,
                CreatedAt = basket.CreatedAt,
                LastModifiedAt = basket.LastModifiedAt
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Basket audit S8 (M3): Redis being down is a 503, like every other Basket read or write.
            _logger.LogError(ex, "Failed to read basket. UserId={UserId}", request.UserId);
            return Result<BasketDto>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}
