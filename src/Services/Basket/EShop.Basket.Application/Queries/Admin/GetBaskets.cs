using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Queries.Admin;

/// <summary><c>GET /api/v1/basket/admin/carts</c> (Admin panel S14, #78): every stored basket, a page at a time.</summary>
public sealed record GetBasketsQuery : BasketScanQuery, IRequest<Result<AdminBasketPageDto>>;

public sealed class GetBasketsQueryValidator : AbstractValidator<GetBasketsQuery>
{
    public GetBasketsQueryValidator() => BasketScanRules.Apply(this);
}

public sealed class GetBasketsQueryHandler : IRequestHandler<GetBasketsQuery, Result<AdminBasketPageDto>>
{
    private readonly IBasketAdminReader _reader;
    private readonly ILogger<GetBasketsQueryHandler> _logger;

    public GetBasketsQueryHandler(IBasketAdminReader reader, ILogger<GetBasketsQueryHandler> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<Result<AdminBasketPageDto>> Handle(GetBasketsQuery request, CancellationToken cancellationToken)
    {
        // The validator has already refused a cursor this cannot parse.
        BasketScanCursor.TryParse(request.Cursor, out var from);

        try
        {
            var page = await _reader.ScanBasketsAsync(from, request.EffectivePageSize, lastModifiedBefore: null, cancellationToken);
            return Result<AdminBasketPageDto>.Success(BasketScanPages.ToDto(page, modifiedBefore: null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Redis being down is a 503, as on every other Basket route (Basket audit S8, M3).
            _logger.LogError(ex, "Failed to list stored baskets");
            return Result<AdminBasketPageDto>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}

internal static class BasketScanPages
{
    public static AdminBasketPageDto ToDto(StoredBasketPage page, DateTime? modifiedBefore) => new()
    {
        Items = page.Entries.Select(AdminBasketSummaryDto.From).ToList(),
        NextCursor = page.Next is { } next ? BasketScanCursor.Format(next) : null,
        ModifiedBefore = modifiedBefore
    };
}
