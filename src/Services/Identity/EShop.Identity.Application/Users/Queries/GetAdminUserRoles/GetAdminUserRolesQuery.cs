using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Interfaces;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetAdminUserRoles;

/// <summary>
/// A user's role names (Admin panel S6, endpoint #15).
/// </summary>
/// <remarks>
/// A separate endpoint from the detail card, which already carries <c>Roles</c>, because S7's role
/// editor needs to re-read just this after a change — and because polling the whole detail card to
/// refresh one list is how a screen ends up re-reading a user's login history every few seconds.
/// </remarks>
public record GetAdminUserRolesQuery : IRequest<Result<IReadOnlyList<string>>>
{
    public string UserId { get; init; } = string.Empty;
}

public class GetAdminUserRolesQueryHandler
    : IRequestHandler<GetAdminUserRolesQuery, Result<IReadOnlyList<string>>>
{
    private readonly IAdminUserQueryService _queryService;

    public GetAdminUserRolesQueryHandler(IAdminUserQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<Result<IReadOnlyList<string>>> Handle(
        GetAdminUserRolesQuery request,
        CancellationToken cancellationToken)
    {
        // Read through the detail projection rather than IUserRepository.GetRolesAsync, which takes
        // an ApplicationUser and so would need the user loaded through UserManager — and that runs
        // under the !IsDeleted filter, hiding exactly the users an admin may be inspecting.
        var user = await _queryService.GetUserDetailAsync(request.UserId, cancellationToken);

        if (user is null)
            return Result<IReadOnlyList<string>>.Failure(
                new Error("User.NotFound", $"User with ID '{request.UserId}' was not found."));

        return Result<IReadOnlyList<string>>.Success(user.Roles);
    }
}
