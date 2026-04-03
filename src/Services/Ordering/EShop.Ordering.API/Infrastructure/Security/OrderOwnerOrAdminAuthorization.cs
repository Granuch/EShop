using EShop.Ordering.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace EShop.Ordering.API.Infrastructure.Security;

public sealed class OrderOwnerOrAdminRequirement : IAuthorizationRequirement;

public sealed class OrderOwnerOrAdminHandler : AuthorizationHandler<OrderOwnerOrAdminRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderRepository _orderRepository;

    public OrderOwnerOrAdminHandler(IHttpContextAccessor httpContextAccessor, IOrderRepository orderRepository)
    {
        _httpContextAccessor = httpContextAccessor;
        _orderRepository = orderRepository;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OrderOwnerOrAdminRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (context.User.IsAdmin())
        {
            context.Succeed(requirement);
            return;
        }

        var subjectId = context.User.GetSubjectId();
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        var routeValue = httpContext.GetRouteValue("id")?.ToString()
            ?? httpContext.GetRouteValue("orderId")?.ToString();

        if (!Guid.TryParse(routeValue, out var orderId))
        {
            return;
        }

        var order = await _orderRepository.GetByIdReadOnlyAsync(orderId, httpContext.RequestAborted);
        if (order is null)
        {
            return;
        }

        if (string.Equals(order.UserId, subjectId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
    }
}
