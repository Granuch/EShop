using Microsoft.AspNetCore.Authorization;

namespace EShop.Basket.API.Infrastructure.Security;

public sealed class SameUserOrAdminRequirement : IAuthorizationRequirement;

public sealed class SameUserOrAdminHandler : AuthorizationHandler<SameUserOrAdminRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SameUserOrAdminHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameUserOrAdminRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (context.User.IsAdmin())
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var subjectId = context.User.GetSubjectId();
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return Task.CompletedTask;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Task.CompletedTask;
        }

        var routeUserId = httpContext.GetRouteValue("userId")?.ToString();

        if (!string.IsNullOrWhiteSpace(routeUserId)
            && string.Equals(routeUserId, subjectId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
