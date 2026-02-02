using EShop.BuildingBlocks.Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EShop.BuildingBlocks.Infrastructure.Data;

/// <summary>
/// Base DbContext for Identity services with domain event dispatching and Unit of Work support
/// Combines ASP.NET Core Identity functionality with our architectural patterns
/// </summary>
public abstract class BaseIdentityDbContext<TUser, TRole, TKey> : IdentityDbContext<TUser, TRole, TKey>, IUnitOfWork
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly IMediator? _mediator;
    private IDbContextTransaction? _currentTransaction;

    protected BaseIdentityDbContext(DbContextOptions options) : base(options)
    {
    }

    protected BaseIdentityDbContext(DbContextOptions options, IMediator mediator) : base(options)
    {
        _mediator = mediator;
    }

    public bool HasActiveTransaction => _currentTransaction != null;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetAuditFields();

        await DispatchDomainEventsAsync(cancellationToken);

        return await base.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            return;
        }

        _currentTransaction = await Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction == null)
        {
            throw new InvalidOperationException("No transaction to commit");
        }

        try
        {
            await SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction == null)
        {
            return;
        }

        try
        {
            await _currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;
        }
    }

    private void SetAuditFields()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            var now = DateTime.UtcNow;

            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.GetType().GetProperty("CreatedAt") is { } createdAtProp)
                {
                    createdAtProp.SetValue(entry.Entity, now);
                }
            }

            if (entry.Entity.GetType().GetProperty("UpdatedAt") is { } updatedAtProp)
            {
                updatedAtProp.SetValue(entry.Entity, now);
            }
        }
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        if (_mediator == null) return;

        var aggregateRoots = ChangeTracker.Entries()
            .Where(e => e.Entity is IAggregateRootMarker)
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = new List<IDomainEvent>();

        foreach (var entity in aggregateRoots)
        {
            var eventsProperty = entity.GetType().GetProperty("DomainEvents");
            if (eventsProperty?.GetValue(entity) is IReadOnlyList<IDomainEvent> events)
            {
                domainEvents.AddRange(events);
            }
        }

        foreach (var entity in aggregateRoots)
        {
            var clearMethod = entity.GetType().GetMethod("ClearDomainEvents");
            clearMethod?.Invoke(entity, null);
        }

        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
    }

    public override void Dispose()
    {
        _currentTransaction?.Dispose();
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_currentTransaction != null)
        {
            await _currentTransaction.DisposeAsync();
        }
        await base.DisposeAsync();
    }
}
