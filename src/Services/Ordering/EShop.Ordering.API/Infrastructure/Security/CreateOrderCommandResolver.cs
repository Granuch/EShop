using EShop.Ordering.Application.Orders.Commands.CreateOrder;

namespace EShop.Ordering.API.Infrastructure.Security;

public static class CreateOrderCommandResolver
{
    public static bool TryResolve(HttpContext httpContext, CreateOrderCommand command, out CreateOrderCommand resolvedCommand, out IResult? error)
    {
        resolvedCommand = command;
        error = null;

        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            error = Results.Unauthorized();
            return false;
        }

        var subjectId = user.GetSubjectId();
        var isAdmin = user.IsAdmin();

        if (!isAdmin)
        {
            if (string.IsNullOrWhiteSpace(subjectId))
            {
                error = Results.Problem(
                    detail: "User identifier not found in authentication claims.",
                    title: "Unauthorized",
                    statusCode: StatusCodes.Status401Unauthorized);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(command.UserId) &&
                !string.Equals(command.UserId, subjectId, StringComparison.OrdinalIgnoreCase))
            {
                error = Results.Problem(
                    detail: "You are not allowed to create orders on behalf of other users.",
                    title: "Forbidden",
                    statusCode: StatusCodes.Status403Forbidden);
                return false;
            }

            resolvedCommand = command with { UserId = subjectId };
            return true;
        }

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            error = Results.Problem(
                detail: "UserId is required for admin order creation.",
                title: "Validation.UserIdRequired",
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        return true;
    }
}
