using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<PaymentTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);
    Task UpdateAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);
    IQueryable<PaymentTransaction> Query();
}
