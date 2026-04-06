using System.Security.Claims;

namespace EShop.Payment.API.Infrastructure.Security;

public static class ClaimsPrincipalExtensions
{
    public static string? GetSubjectId(this ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("uid")?.Value;
    }

    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole("Admin");
    }
}
