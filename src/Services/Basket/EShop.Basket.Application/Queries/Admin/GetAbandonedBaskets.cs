using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Queries.Admin;

/// <summary>
/// <c>GET /api/v1/basket/admin/abandoned?olderThan=24h</c> (Admin panel S14, #79): baskets nobody has changed for at
/// least that long. "Changed" is the basket's own <c>LastModifiedAt</c>, which every write through the domain moves —
/// price sync included, since a re-price is a change the customer will see. An unreadable basket has no age to judge,
/// so it is never listed here; <c>/carts</c> shows it.
///
/// <para>There is no index by age in Redis, so this is the same walk as <c>/carts</c> with a filter: expect short pages
/// with a <c>nextCursor</c> when few baskets qualify.</para>
/// </summary>
public sealed record GetAbandonedBasketsQuery : BasketScanQuery, IRequest<Result<AdminBasketPageDto>>
{
    /// <summary>A whole number and a unit: <c>90m</c>, <c>24h</c>, <c>3d</c>. Defaults to <c>24h</c>.</summary>
    public string? OlderThan { get; init; }

    public string EffectiveOlderThan => string.IsNullOrEmpty(OlderThan) ? BasketAge.Default : OlderThan;
}

public sealed class GetAbandonedBasketsQueryValidator : AbstractValidator<GetAbandonedBasketsQuery>
{
    public GetAbandonedBasketsQueryValidator()
    {
        BasketScanRules.Apply(this);

        RuleFor(x => x.EffectiveOlderThan)
            .Must(value => BasketAge.TryParse(value, out _))
            .OverridePropertyName(nameof(GetAbandonedBasketsQuery.OlderThan))
            .WithMessage($"olderThan must be a whole number of minutes, hours or days (90m, 24h, 3d), at most {BasketAge.Max.TotalDays:0} days.");
    }
}

public sealed class GetAbandonedBasketsQueryHandler : IRequestHandler<GetAbandonedBasketsQuery, Result<AdminBasketPageDto>>
{
    private readonly IBasketAdminReader _reader;
    private readonly TimeProvider _time;
    private readonly ILogger<GetAbandonedBasketsQueryHandler> _logger;

    public GetAbandonedBasketsQueryHandler(
        IBasketAdminReader reader,
        TimeProvider time,
        ILogger<GetAbandonedBasketsQueryHandler> logger)
    {
        _reader = reader;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<AdminBasketPageDto>> Handle(GetAbandonedBasketsQuery request, CancellationToken cancellationToken)
    {
        // Both already accepted by the validator.
        BasketScanCursor.TryParse(request.Cursor, out var from);
        BasketAge.TryParse(request.EffectiveOlderThan, out var olderThan);

        // Stored dates are UTC (DateTime.UtcNow in the domain), so the cutoff is too.
        var modifiedBefore = _time.GetUtcNow().UtcDateTime - olderThan;

        try
        {
            var page = await _reader.ScanBasketsAsync(from, request.EffectivePageSize, modifiedBefore, cancellationToken);
            return Result<AdminBasketPageDto>.Success(BasketScanPages.ToDto(page, modifiedBefore));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list abandoned baskets");
            return Result<AdminBasketPageDto>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}
