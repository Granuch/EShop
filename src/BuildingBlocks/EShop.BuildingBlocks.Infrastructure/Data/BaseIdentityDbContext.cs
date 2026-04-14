using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Outbox;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace EShop.BuildingBlocks.Infrastructure.Data;

/// <summary>
/// Base DbContext for Identity services with domain event dispatching via Outbox pattern,
/// Unit of Work support, and automatic audit field population (CreatedBy, UpdatedBy).
/// Combines ASP.NET Core Identity functionality with our architectural patterns.
/// </summary>
public abstract class BaseIdentityDbContext<TUser, TRole, TKey> : IdentityDbContext<TUser, TRole, TKey>, IUnitOfWork
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly IMediator? _mediator;
    private readonly ICurrentUserContext? _currentUserContext;
    private IDbContextTransaction? _currentTransaction;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// When true, uses the Outbox pattern for reliable event delivery.
    /// When false, publishes events directly via MediatR (for backward compatibility or testing).
    /// Default is false for Identity service since it primarily uses ASP.NET Identity.
    /// </summary>
    protected virtual bool UseOutbox => false;

    /// <summary>
    /// Outbox messages DbSet. Override this in derived contexts if you need a custom table name.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected BaseIdentityDbContext(DbContextOptions options) : base(options)
    {
    }

    protected BaseIdentityDbContext(DbContextOptions options, IMediator mediator) : base(options)
    {
        _mediator = mediator;
    }

    protected BaseIdentityDbContext(DbContextOptions options, IMediator mediator, ICurrentUserContext currentUserContext) : base(options)
    {
        _mediator = mediator;
        _currentUserContext = currentUserContext;
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

        // In-memory database doesn't support real transactions, skip it
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return;
        }

        _currentTransaction = await Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        // For in-memory database, just save changes without transaction
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            const int maxConcurrencyRetries = 3;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await SaveChangesAsync(cancellationToken);
                    return;
                }
                catch (DbUpdateConcurrencyException ex) when (attempt < maxConcurrencyRetries)
                {
                    // In-memory database may have stale entities from prior operations
                    // (e.g., UserManager.UpdateAsync catches concurrency exceptions internally
                    // but leaves entities in Modified state). Reload conflicting entities
                    // and retry the save a bounded number of times.
                    foreach (var entry in ex.Entries)
                    {
                        await entry.ReloadAsync(cancellationToken);
                    }
                }
            }
        }

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
        // In-memory database doesn't support transactions
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return;
        }

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

        var now = DateTime.UtcNow;
        var currentUserId = _currentUserContext?.UserId;

        foreach (var entry in entries)
        {
            var entityType = entry.Entity.GetType();

            if (entry.State == EntityState.Added)
            {
                // Set CreatedAt
                if (entityType.GetProperty("CreatedAt") is { } createdAtProp 
                    && createdAtProp.CanWrite)
                {
                    createdAtProp.SetValue(entry.Entity, now);
                }

                // Set CreatedBy (only if we have a user context and property exists)
                if (entityType.GetProperty("CreatedBy") is { } createdByProp 
                    && createdByProp.CanWrite 
                    && currentUserId != null)
                {
                    createdByProp.SetValue(entry.Entity, currentUserId);
                }
            }

            // Set UpdatedAt (always on modification)
            if (entityType.GetProperty("UpdatedAt") is { } updatedAtProp 
                && updatedAtProp.CanWrite)
            {
                updatedAtProp.SetValue(entry.Entity, now);
            }

            // Set UpdatedBy (on modification, if we have a user context)
            if (entry.State == EntityState.Modified 
                && entityType.GetProperty("UpdatedBy") is { } updatedByProp 
                && updatedByProp.CanWrite 
                && currentUserId != null)
            {
                updatedByProp.SetValue(entry.Entity, currentUserId);
            }
        }
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        var aggregateRoots = ChangeTracker.Entries()
            .Where(e => e.Entity is IAggregateRootMarker)
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = new List<(IDomainEvent Event, string? AggregateType, string? AggregateId)>();

        foreach (var entity in aggregateRoots)
        {
            var eventsProperty = entity.GetType().GetProperty("DomainEvents");
            if (eventsProperty?.GetValue(entity) is IReadOnlyList<IDomainEvent> events && events.Count > 0)
            {
                var aggregateType = entity.GetType().Name;
                var aggregateId = entity.GetType().GetProperty("Id")?.GetValue(entity)?.ToString();

                foreach (var evt in events)
                {
                    domainEvents.Add((evt, aggregateType, aggregateId));
                }
            }
        }

        // Clear events from all aggregates
        foreach (var entity in aggregateRoots)
        {
            var clearMethod = entity.GetType().GetMethod("ClearDomainEvents");
            clearMethod?.Invoke(entity, null);
        }

        if (domainEvents.Count == 0)
        {
            return;
        }

        var correlationId = _currentUserContext?.CorrelationId;

        if (UseOutbox)
        {
            // Outbox pattern: Persist events to the outbox table
            foreach (var (domainEvent, aggregateType, aggregateId) in domainEvents)
            {
                var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions);
                var outboxMessage = OutboxMessage.Create(
                    domainEvent,
                    payload,
                    correlationId,
                    aggregateType,
                    aggregateId);

                OutboxMessages.Add(outboxMessage);
            }
        }
        else if (_mediator != null)
        {
            // Direct publishing (for backward compatibility or in-memory testing)
            foreach (var (domainEvent, _, _) in domainEvents)
            {
                await _mediator.Publish(domainEvent, cancellationToken);
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply Outbox and ProcessedMessage configurations
        modelBuilder.ApplyConfiguration(new Configurations.OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.ProcessedMessageConfiguration());
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
