using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetAdminUserStats;

/// <summary>
/// The dashboard user tile (Admin panel S6, endpoint #19).
/// </summary>
/// <remarks>
/// <c>From</c>/<c>To</c> bound <b>only</b> <see cref="AdminUserStatsDto.NewInPeriod"/>. The tile
/// reads "N users, M new this month", so applying the window to the other counts would make every
/// figure mean something different from its label — "active" would become "active and created this
/// month", which is not a number anyone wants.
/// </remarks>
public record GetAdminUserStatsQuery : IRequest<Result<AdminUserStatsDto>>
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

/// <param name="Total">Live users. Excludes soft-deleted ones, which are counted separately.</param>
/// <param name="NewInPeriod">Live users created within <c>from</c>..<c>to</c>; all of them when neither is given.</param>
/// <param name="Locked">Live users whose lockout is in force <i>right now</i>, not merely ever set.</param>
/// <param name="Unconfirmed">Live users who have not confirmed their email.</param>
public sealed record AdminUserStatsDto(
    int Total,
    int NewInPeriod,
    int Active,
    int Locked,
    int Unconfirmed,
    int Deleted);

public class GetAdminUserStatsQueryHandler
    : IRequestHandler<GetAdminUserStatsQuery, Result<AdminUserStatsDto>>
{
    private readonly IAdminUserQueryService _queryService;

    public GetAdminUserStatsQueryHandler(IAdminUserQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<Result<AdminUserStatsDto>> Handle(
        GetAdminUserStatsQuery request,
        CancellationToken cancellationToken)
    {
        var stats = await _queryService.GetUserStatsAsync(request.From, request.To, cancellationToken);

        return Result<AdminUserStatsDto>.Success(new AdminUserStatsDto(
            stats.Total,
            stats.NewInPeriod,
            stats.Active,
            stats.Locked,
            stats.Unconfirmed,
            stats.Deleted));
    }
}

public class GetAdminUserStatsQueryValidator : AbstractValidator<GetAdminUserStatsQuery>
{
    public GetAdminUserStatsQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
                .WithMessage("'from' must not be after 'to'");
    }
}
