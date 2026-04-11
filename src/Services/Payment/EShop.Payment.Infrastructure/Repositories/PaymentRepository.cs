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

    public async Task AddCustomerAsync(PaymentCustomer customer, CancellationToken cancellationToken = default)
    {
        await _context.PaymentCustomers.AddAsync(customer, cancellationToken);
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

    public IQueryable<PaymentTransaction> Query()
    {
        return _context.PaymentTransactions.AsNoTracking();
    }
}
