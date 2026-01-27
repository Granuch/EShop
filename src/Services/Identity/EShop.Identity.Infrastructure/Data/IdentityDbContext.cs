using Microsoft.EntityFrameworkCore;
using EShop.BuildingBlocks.Infrastructure.Data;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace EShop.Identity.Infrastructure.Data;

/// <summary>
/// DbContext for Identity service
/// </summary>
public class IdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // TODO: Configure ApplicationUser entity
        // builder.Entity<ApplicationUser>(entity => { ... });

        // TODO: Configure ApplicationRole entity
        // builder.Entity<ApplicationRole>(entity => { ... });

        // TODO: Apply custom configurations from assembly
        // builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // TODO: Seed default roles and admin user
    }
}
