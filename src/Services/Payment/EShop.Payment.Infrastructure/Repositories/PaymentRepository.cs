using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _context;

    public PaymentRepository(PaymentDbContext context)
    {
        _context = context;
    }

    public Task<PaymentTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _context.PaymentTransactions
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return _context.PaymentTransactions
            .FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
    }

    public Task<PaymentTransaction?> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        return _context.PaymentTransactions
            .FirstOrDefaultAsync(x => x.PaymentIntentId == paymentIntentId, cancellationToken);
    }

    public Task<List<PaymentTransaction>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return _context.PaymentTransactions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
    {
        await _context.PaymentTransactions.AddAsync(payment, cancellationToken);
    }

    public Task<PaymentCustomer?> GetCustomerByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return _context.PaymentCustomers
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// Payment audit Stage 6 (M2). Runs at once, in the current transaction, and never violates the unique
    /// <c>UserId</c> index: if another transaction holds an uncommitted mapping for the user, this one waits for it,
    /// then keeps it. Postgres-only SQL, which the InMemory provider cannot run, so InMemory tests mock this repository
    /// method or the customer service.
    /// </summary>
    public async Task<PaymentCustomer> AddCustomerIfAbsentAsync(PaymentCustomer customer, CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PaymentCustomers" ("Id", "UserId", "StripeCustomerId", "CreatedAt", "UpdatedAt")
            VALUES ({customer.Id}, {customer.UserId}, {customer.StripeCustomerId}, {customer.CreatedAt}, {customer.UpdatedAt ?? customer.CreatedAt})
            ON CONFLICT ("UserId") DO NOTHING
            """, cancellationToken);

        return await _context.PaymentCustomers
            .AsNoTracking()
            .SingleAsync(x => x.UserId == customer.UserId, cancellationToken);
    }

    public Task<bool> IsStripeEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
    {
        return _context.ProcessedStripeWebhookEvents
            .AnyAsync(x => x.EventId == eventId, cancellationToken);
    }

    public async Task AddProcessedStripeEventAsync(ProcessedStripeWebhookEvent processedEvent, CancellationToken cancellationToken = default)
    {
        await _context.ProcessedStripeWebhookEvents.AddAsync(processedEvent, cancellationToken);
    }

    public Task UpdateAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(payment);
        if (entry.State == EntityState.Detached)
        {
            _context.PaymentTransactions.Attach(payment);
            entry.State = EntityState.Modified;
        }

        return Task.CompletedTask;
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Payment audit D7. Catching the conflict is only safe with no transaction open: EF rolls back its own
        // SaveChanges transaction, whereas inside an outer one Postgres would refuse every later statement (25P02).
        if (_context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "TrySaveChangesAsync must not run inside a transaction: a lost save there leaves the transaction aborted.");
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<PaymentTransaction?> GetCurrentByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return _context.PaymentTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
    }

    public IQueryable<PaymentTransaction> Query()
    {
        return _context.PaymentTransactions.AsNoTracking();
    }
}
