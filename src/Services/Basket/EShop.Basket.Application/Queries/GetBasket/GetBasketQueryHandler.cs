using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Basket.Application.Queries.GetBasket;

public sealed class GetBasketQueryHandler : IRequestHandler<GetBasketQuery, Result<BasketDto?>>
{
    private readonly IBasketRepository _basketRepository;

    public GetBasketQueryHandler(IBasketRepository basketRepository)
    {
        _basketRepository = basketRepository;
    }

    public async Task<Result<BasketDto?>> Handle(GetBasketQuery request, CancellationToken cancellationToken)
    {
        var basket = await _basketRepository.GetBasketAsync(request.UserId, cancellationToken);
        if (basket == null)
        {
            return Result<BasketDto?>.Success(null);
        }

        var dto = new BasketDto
        {
            UserId = basket.UserId,
            Items = basket.Items.Select(item => new BasketItemDto
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Price = item.Price,
                Quantity = item.Quantity,
                SubTotal = item.SubTotal
            }).ToList(),
            TotalPrice = basket.TotalPrice,
            TotalItems = basket.TotalItems,
            CreatedAt = basket.CreatedAt,
            LastModifiedAt = basket.LastModifiedAt
        };

        return Result<BasketDto?>.Success(dto);
    }
}
