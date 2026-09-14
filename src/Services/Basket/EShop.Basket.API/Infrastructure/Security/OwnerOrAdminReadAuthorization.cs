using Microsoft.AspNetCore.Authorization;

namespace EShop.Basket.API.Infrastructure.Security;

/// <summary>
/// Who may use <c>/api/v1/basket/{userId}</c> (Basket audit S10, D10 and L9):
/// <list type="bullet">
/// <item>the basket's owner, for everything: the token's subject must equal the route's <c>userId</c> exactly;</item>
/// <item>an admin, only to read (GET or HEAD) another user's basket.</item>
/// </list>
/// The policy was <c>SameUserOrAdmin</c>, and an admin passed it for every route — checkout included, which placed an
/// order for another user at an address the admin chose. Deciding by HTTP method keeps any new write endpoint in the group
/// owner-only without anyone having to remember it.
/// </summary>
public sealed class OwnerOrAdminReadRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "BasketOwnerOrAdminRead";
}

public sealed class OwnerOrAdminReadHandler : AuthorizationHandler<OwnerOrAdminReadRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OwnerOrAdminReadHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OwnerOrAdminReadRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Task.CompletedTask;
        }

        var routeUserId = httpContext.GetRouteValue("userId")?.ToString();
        var subjectId = context.User.GetSubjectId();

        // Ordinal (L9): the basket's Redis keys are built from this id and are case-sensitive, so an id that differs only
        // in case names another basket. The comparison used to ignore case, so both casings passed and addressed two
        // different baskets for one user.
        if (!string.IsNullOrWhiteSpace(routeUserId)
            && !string.IsNullOrWhiteSpace(subjectId)
            && string.Equals(routeUserId, subjectId, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // D10: an admin may look at any basket, for support, and change none but their own.
        var method = httpContext.Request.Method;
        if (context.User.IsAdmin() && (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
