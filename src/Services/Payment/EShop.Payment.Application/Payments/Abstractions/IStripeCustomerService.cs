namespace EShop.Payment.Application.Payments.Abstractions;

public interface IStripeCustomerService
{
    Task<string> CreateOrGetCustomerAsync(string userId, string? email, CancellationToken cancellationToken = default);
}
