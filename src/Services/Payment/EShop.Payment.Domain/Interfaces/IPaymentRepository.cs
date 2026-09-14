using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<PaymentTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default);
    /// <summary>
    /// One page of the user's payments, newest first, and how many there are in all (Payment audit S10: D10, D11). The
    /// placeholder a cancellation leaves when it overtakes the order (method None, nothing charged) is not listed.
    /// </summary>
    Task<(List<PaymentTransaction> Items, int TotalCount)> GetPageByUserIdAsync(
        string userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<PaymentCustomer?> GetCustomerByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<PaymentCustomer> AddCustomerIfAbsentAsync(PaymentCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves pending changes, unless another writer changed a payment this save updates since it was read (its row
    /// version). Then nothing is saved, the tracked changes are dropped, and the result is false. Payment audit D7: only
    /// with no transaction open. After a failed save inside one, Postgres refuses every later statement.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>The payment for the order as the database holds it now, untracked.</summary>
    Task<PaymentTransaction?> GetCurrentByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<bool> IsStripeEventProcessedAsync(string eventId, CancellationToken cancellationToken = default);
    Task AddProcessedStripeEventAsync(ProcessedStripeWebhookEvent processedEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the webhook events recorded before <paramref name="cutoff"/>, at once, and returns how many (Payment audit
    /// D13). Relational only.
    /// </summary>
    Task<int> DeleteProcessedStripeEventsBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default);
    Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);
    Task UpdateAsync(PaymentTransaction payment, CancellationToken cancellationToken = default);
}
