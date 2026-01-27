using EShop.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace EShop.BuildingBlocks.Infrastructure.Data;

/// <summary>
/// Base DbContext with domain event dispatching support
/// </summary>
public abstract class BaseDbContext : DbContext
{
    protected BaseDbContext(DbContextOptions options) : base(options)
    {
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Set audit fields (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
        // TODO: Dispatch domain events before saving
        // TODO: Implement outbox pattern for reliable messaging
        
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TODO: Apply all configurations from current assembly
        // modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        
        base.OnModelCreating(modelBuilder);
    }
}
