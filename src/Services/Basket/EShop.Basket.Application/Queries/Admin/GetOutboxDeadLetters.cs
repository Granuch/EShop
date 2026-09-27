using EShop.Basket.Application.Common;
using EShop.Basket.Domain.Interfaces;
using EShop.BuildingBlocks.Application;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Application.Queries.Admin;

/// <summary>
/// <c>GET /api/v1/basket/admin/outbox/dead-letters/details</c> (Admin panel S14, #81): the dead letters themselves,
/// newest first. Offset paging, because this is a Redis list read with <c>LRANGE</c>: a replay or a new dead letter
/// between two pages shifts the list, so a page can repeat or skip an entry — <see cref="OutboxDeadLetterPageDto.Total"/>
/// is the check.
/// </summary>
public sealed record GetOutboxDeadLettersQuery : IRequest<Result<OutboxDeadLetterPageDto>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public int? Offset { get; init; }
    public int? Limit { get; init; }

    public int EffectiveOffset => Offset ?? 0;
    public int EffectiveLimit => Limit ?? DefaultLimit;
}

public sealed class GetOutboxDeadLettersQueryValidator : AbstractValidator<GetOutboxDeadLettersQuery>
{
    public GetOutboxDeadLettersQueryValidator()
    {
        RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Offset.HasValue);

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, GetOutboxDeadLettersQuery.MaxLimit)
            .When(x => x.Limit.HasValue);
    }
}

public sealed class GetOutboxDeadLettersQueryHandler
    : IRequestHandler<GetOutboxDeadLettersQuery, Result<OutboxDeadLetterPageDto>>
{
    private readonly IOutboxDeadLetterReader _reader;
    private readonly ILogger<GetOutboxDeadLettersQueryHandler> _logger;

    public GetOutboxDeadLettersQueryHandler(IOutboxDeadLetterReader reader, ILogger<GetOutboxDeadLettersQueryHandler> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<Result<OutboxDeadLetterPageDto>> Handle(
        GetOutboxDeadLettersQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _reader.ReadDeadLettersAsync(request.EffectiveOffset, request.EffectiveLimit, cancellationToken);

            return Result<OutboxDeadLetterPageDto>.Success(new OutboxDeadLetterPageDto
            {
                Total = page.Total,
                Offset = request.EffectiveOffset,
                Limit = request.EffectiveLimit,
                Items = page.Entries.Select(OutboxDeadLetterDto.From).ToList()
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the basket outbox's dead letters");
            return Result<OutboxDeadLetterPageDto>.Failure(BasketErrors.BasketOperationFailed);
        }
    }
}
