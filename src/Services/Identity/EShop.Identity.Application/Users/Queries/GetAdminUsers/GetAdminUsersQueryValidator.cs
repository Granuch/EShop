using EShop.Identity.Domain.Interfaces;
using FluentValidation;

namespace EShop.Identity.Application.Users.Queries.GetAdminUsers;

/// <summary>
/// Validator for GetAdminUsersQuery. The page-size cap matters more here than in a product list:
/// each page costs a second batch query for its roles, and an uncapped page would make that a
/// table scan of <c>user_roles</c>.
/// </summary>
public class GetAdminUsersQueryValidator : AbstractValidator<GetAdminUsersQuery>
{
    private static readonly string[] SortNames = Enum.GetNames<AdminUserSortBy>();

    public GetAdminUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0")
            .When(x => x.PageNumber.HasValue);

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100")
            .When(x => x.PageSize.HasValue);

        RuleFor(x => x.Search)
            .MaximumLength(256).WithMessage("Search term must not exceed 256 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Search));

        RuleFor(x => x.Role)
            .MaximumLength(256).WithMessage("Role name must not exceed 256 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Role));

        // Frontend-contracts F-01: a name in any case, never a number. MVC's enum binder took "1" as well, and the
        // OpenAPI document typed the parameter as an integer.
        RuleFor(x => x.SortBy)
            .Must(sortBy => SortNames.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"SortBy must be one of: {string.Join(", ", SortNames)}")
            .When(x => !string.IsNullOrEmpty(x.SortBy));

        RuleFor(x => x)
            .Must(x => x.CreatedFrom is null || x.CreatedTo is null || x.CreatedFrom <= x.CreatedTo)
                .WithMessage("'createdFrom' must not be after 'createdTo'")
            .Must(x => x.LastLoginFrom is null || x.LastLoginTo is null || x.LastLoginFrom <= x.LastLoginTo)
                .WithMessage("'lastLoginFrom' must not be after 'lastLoginTo'");
    }
}
