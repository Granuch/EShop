using System.Security.Cryptography;
using System.Text;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Stripe;

namespace EShop.Payment.Infrastructure.Services;

public sealed class StripeCustomerService : IStripeCustomerService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IStripeClient _client;

    /// <summary>
    /// Payment audit Stage 9 (M4). Stripe is called through the injected client. This service used Stripe.net's
    /// process-wide client, which had a key only if a <c>StripePaymentService</c> had been built earlier in the process.
    /// </summary>
    public StripeCustomerService(IPaymentRepository paymentRepository, IStripeClient client)
    {
        _paymentRepository = paymentRepository;
        _client = client;
    }

    public async Task<string> CreateOrGetCustomerAsync(string userId, string? email, CancellationToken cancellationToken = default)
    {
        var existing = await _paymentRepository.GetCustomerByUserIdAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing.StripeCustomerId;
        }

        email = string.IsNullOrWhiteSpace(email) ? null : email;

        Customer created;
        try
        {
            // Payment audit Stage 6 (M1). The request can fail after Stripe has created the customer and before the
            // mapping below is stored. The key makes the retry get the same Stripe customer back (for 24 hours) instead
            // of a second one.
            created = await new CustomerService(_client).CreateAsync(
                new CustomerCreateOptions
                {
                    Email = email,
                    Metadata = new Dictionary<string, string> { ["userId"] = userId }
                },
                new RequestOptions { IdempotencyKey = CustomerIdempotencyKey(userId, email) },
                cancellationToken);
        }
        catch (Exception ex) when (StripeErrors.IsTransient(ex, cancellationToken))
        {
            throw new PaymentProviderUnavailableException("create customer", ex);
        }

        // Payment audit Stage 6 (M2). This used to insert through EF and catch the DbUpdateException that a concurrent
        // first checkout by the same user raises. Inside the caller's transaction that cannot work: Postgres aborts the
        // transaction on the unique violation, so the re-read failed with 25P02 and the checkout failed after all. The
        // upsert never violates the index. The loser waits for the winner, then reads the winner's mapping.
        var stored = await _paymentRepository.AddCustomerIfAbsentAsync(new PaymentCustomer
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StripeCustomerId = created.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken);

        return stored.StripeCustomerId;
    }

    /// <summary>
    /// Stripe refuses a key reused with different parameters, and the e-mail is a parameter, so it is part of the key.
    /// A different e-mail on the retry creates a second customer, and the upsert keeps whichever mapping was stored first.
    /// </summary>
    public static string CustomerIdempotencyKey(string userId, string? email)
    {
        var emailHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email ?? string.Empty)))[..16];
        return $"customer-{userId}-{emailHash}";
    }
}
