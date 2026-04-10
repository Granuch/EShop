using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<PaymentTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default);
    Task<List<PaymentTransaction>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<PaymentCustomer?> GetCustomerByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task AddCustomerAsync(PaymentCustomer customer, CancellationToken cancellationToken = default);
    Task<bool> IsStripeEventProcessedAsync(string eventId, CancellationToken cancellationToken = default);
    Task AddProcessedStripeEventAsync(ProcessedStripeWebhookEvent processedEvent, CancellationToken cancellationToken = default);
    Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);
    Task UpdateAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);
    IQueryable<PaymentTransaction> Query();
}
