using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Payment.IntegrationTests.Fixtures;

/// <summary>
/// Admin panel S11 (endpoint #67). The Stripe-enabled host with a switch that makes webhook processing fail
/// <b>after</b> it has decided what to do — <see cref="IPaymentRepository.UpdateAsync"/> throws, with the payment's
/// transition and its new timeline row already pending in the request's change tracker.
///
/// <para>
/// That is the shape the capture has to survive, not a failure before any work: the endpoint's own DbContext is dirty
/// with changes that must never reach the database, so a capture written through it would either be lost with them or
/// — worse, and this is what the falsification round shows — commit them.
/// </para>
///
/// <para>
/// The failure is a switch rather than a permanent decoration so one fixture can fail a delivery, then let its replay
/// succeed, which is the whole story #67 exists to tell.
/// </para>
/// </summary>
public sealed class FailingWebhookPaymentApiFactory : StripeEnabledPaymentApiFactory
{
    /// <summary>
    /// Which payments a webhook fails on, applied just after the payment has been changed in memory. A predicate
    /// rather than a flag so one fixture can leave one capture poisoned while another replays cleanly — which is how
    /// "one bad row does not decide the batch" is shown at all.
    /// </summary>
    public Func<PaymentTransaction, bool> FailWhen { get; set; } = _ => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentRepository>();
            services.AddScoped<IPaymentRepository>(provider => new FailingOnUpdateRepository(
                new PaymentRepository(provider.GetRequiredService<PaymentDbContext>()),
                this));
        });
    }

    private sealed class FailingOnUpdateRepository(PaymentRepository inner, FailingWebhookPaymentApiFactory owner)
        : IPaymentRepository
    {
        public Task UpdateAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
            => owner.FailWhen(payment)
                ? throw new InvalidOperationException("the database went away mid-webhook")
                : inner.UpdateAsync(payment, cancellationToken);

        public Task<PaymentTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.GetByIdAsync(id, cancellationToken);

        public Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
            => inner.GetByOrderIdAsync(orderId, cancellationToken);

        public Task<PaymentTransaction?> GetByPaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default)
            => inner.GetByPaymentIntentIdAsync(paymentIntentId, cancellationToken);

        public Task<(List<PaymentTransaction> Items, int TotalCount)> GetPageByUserIdAsync(
            string userId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
            => inner.GetPageByUserIdAsync(userId, pageNumber, pageSize, cancellationToken);

        public Task<PaymentCustomer?> GetCustomerByUserIdAsync(string userId, CancellationToken cancellationToken = default)
            => inner.GetCustomerByUserIdAsync(userId, cancellationToken);

        public Task<PaymentCustomer> AddCustomerIfAbsentAsync(PaymentCustomer customer, CancellationToken cancellationToken = default)
            => inner.AddCustomerIfAbsentAsync(customer, cancellationToken);

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
            => inner.TrySaveChangesAsync(cancellationToken);

        public Task<PaymentTransaction?> GetCurrentByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
            => inner.GetCurrentByOrderIdAsync(orderId, cancellationToken);

        public Task<bool> IsStripeEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
            => inner.IsStripeEventProcessedAsync(eventId, cancellationToken);

        public Task AddProcessedStripeEventAsync(ProcessedStripeWebhookEvent processedEvent, CancellationToken cancellationToken = default)
            => inner.AddProcessedStripeEventAsync(processedEvent, cancellationToken);

        public Task<int> DeleteProcessedStripeEventsBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default)
            => inner.DeleteProcessedStripeEventsBeforeAsync(cutoff, cancellationToken);

        public Task AddAsync(PaymentTransaction payment, CancellationToken cancellationToken = default)
            => inner.AddAsync(payment, cancellationToken);
    }
}
