namespace EShop.Payment.Domain.Interfaces;

public interface IStripeCustomerService
{
    Task<string> CreateOrGetCustomerAsync(string userId, string? email, CancellationToken cancellationToken = default);
}
