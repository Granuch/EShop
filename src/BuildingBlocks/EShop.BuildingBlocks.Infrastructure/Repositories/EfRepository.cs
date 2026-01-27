using EShop.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace EShop.BuildingBlocks.Infrastructure.Repositories;

/// <summary>
/// Generic repository implementation using EF Core
/// </summary>
public class EfRepository<T, TId> : IRepository<T, TId> where T : AggregateRoot<TId>
{
    protected readonly DbContext _context;
    protected readonly DbSet<T> _dbSet;

    public EfRepository(DbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        // TODO: Implement with tracking/no-tracking options
        // TODO: Include related entities if needed
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement with pagination support
        // TODO: Consider adding filters for soft-deleted entities
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<T>> FindAsync(
        Expression<Func<T, bool>> predicate, 
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement with predicate filtering
        throw new NotImplementedException();
    }

    public async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        // TODO: Add entity to DbSet
        // TODO: Return added entity
        throw new NotImplementedException();
    }

    public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        // TODO: Update entity
        // TODO: Handle concurrency conflicts
        throw new NotImplementedException();
    }

    public async Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        // TODO: Implement soft delete vs hard delete
        throw new NotImplementedException();
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Delegate to DbContext.SaveChangesAsync
        throw new NotImplementedException();
    }
}
