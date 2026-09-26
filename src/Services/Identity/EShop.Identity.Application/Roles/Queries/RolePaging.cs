using FluentValidation;

namespace EShop.Identity.Application.Roles.Queries;

/// <summary>
/// The paging both role lists take (frontend-contracts F-12): <c>pageNumber</c>/<c>pageSize</c>, as
/// every other list in the platform names them, answered as a <c>PagedResult</c>.
/// </summary>
/// <remarks>
/// Before F-12 these were <c>page</c>/<c>pageSize</c> with no validator and a bare array back, so a
/// client could not tell the last page from a full one, <c>pageSize=100000</c> was served, and
/// <c>page=0</c> or a negative size reached Postgres as a negative <c>OFFSET</c>/<c>LIMIT</c> and
/// answered 500.
/// </remarks>
public interface IRolePageQuery
{
    int? PageNumber { get; }
    int? PageSize { get; }
}

public static class RolePaging
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static int EffectivePageNumber(this IRolePageQuery query) => query.PageNumber ?? 1;

    public static int EffectivePageSize(this IRolePageQuery query) => query.PageSize ?? DefaultPageSize;

    /// <summary>
    /// The rules, generic over the query so each validator is registered for its own type —
    /// FluentValidation resolves a validator by the validated type, so one written for the interface
    /// would never run.
    /// </summary>
    public static void Apply<T>(AbstractValidator<T> validator) where T : IRolePageQuery
    {
        validator.RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0")
            .When(x => x.PageNumber.HasValue);

        validator.RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0")
            .LessThanOrEqualTo(MaxPageSize).WithMessage($"Page size must not exceed {MaxPageSize}")
            .When(x => x.PageSize.HasValue);
    }
}
