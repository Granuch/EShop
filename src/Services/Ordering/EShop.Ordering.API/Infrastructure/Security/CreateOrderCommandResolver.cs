using EShop.BuildingBlocks.Infrastructure.Http;
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
                error = ProblemResults.For(
                    "Unauthorized",
                    "User identifier not found in authentication claims.",
                    StatusCodes.Status401Unauthorized);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(command.UserId) &&
                !string.Equals(command.UserId, subjectId, StringComparison.OrdinalIgnoreCase))
            {
                error = ProblemResults.For(
                    "Forbidden",
                    "You are not allowed to create orders on behalf of other users.",
                    StatusCodes.Status403Forbidden);
                return false;
            }

            resolvedCommand = command with { UserId = subjectId };
            return true;
        }

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            error = ProblemResults.For(
                "Validation.UserIdRequired",
                "UserId is required for admin order creation.",
                StatusCodes.Status400BadRequest);
            return false;
        }

        return true;
    }
}
