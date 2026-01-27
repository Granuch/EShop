using System.Linq.Expressions;

namespace EShop.BuildingBlocks.Domain;

/// <summary>
/// Generic repository pattern interface
/// </summary>
public interface IRepository<T, TId> where T : AggregateRoot<TId>
{
    // TODO: Implement query methods
    Task<T?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    
    // TODO: Implement filtering and pagination
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    
    // TODO: Implement add/update/delete operations
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
    
    // TODO: Implement unit of work pattern
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
