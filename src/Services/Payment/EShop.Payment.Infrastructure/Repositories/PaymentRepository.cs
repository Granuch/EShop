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

    public async Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
    {
        await _context.PaymentTransactions.AddAsync(payment, cancellationToken);
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
