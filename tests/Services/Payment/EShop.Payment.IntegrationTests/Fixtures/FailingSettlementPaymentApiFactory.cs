using EShop.Payment.Domain.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EShop.Payment.IntegrationTests.Fixtures;

/// <summary>
/// Payment audit Stage 10 (M6). The host with a simulator that throws <paramref name="failure"/>, so an admin settling a
/// payment ends with that exception, as if saving had failed that way. That lets the tests see how the service reports
/// each kind of persistence failure, through its real exception mapping.
/// </summary>
public sealed class FailingSettlementPaymentApiFactory(Exception failure) : PaymentApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentProcessor>();
            services.AddSingleton<IPaymentProcessor>(new ThrowingProcessor(failure));
        });
    }

    private sealed class ThrowingProcessor(Exception failure) : IPaymentProcessor
    {
        public Task<PaymentResult> ProcessPaymentAsync(Guid orderId, decimal amount, CancellationToken cancellationToken = default)
            => Task.FromException<PaymentResult>(failure);

        public Task<PaymentResult> RefundPaymentAsync(string paymentIntentId, decimal amount, CancellationToken cancellationToken = default)
            => Task.FromException<PaymentResult>(failure);
    }
}
