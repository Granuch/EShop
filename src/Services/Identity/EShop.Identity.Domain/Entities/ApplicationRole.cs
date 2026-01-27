using Microsoft.AspNetCore.Identity;

namespace EShop.Identity.Domain.Entities;

/// <summary>
/// Extended role entity
/// </summary>
public class ApplicationRole : IdentityRole
{
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    // TODO: Add role permissions/claims management
}
