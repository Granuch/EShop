using EShop.BuildingBlocks.Domain;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Stripe;

namespace EShop.Payment.Infrastructure.Services;

public sealed class StripeCustomerService : IStripeCustomerService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StripeCustomerService> _logger;

    public StripeCustomerService(
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork,
        ILogger<StripeCustomerService> logger)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<string> CreateOrGetCustomerAsync(string userId, string? email, CancellationToken cancellationToken = default)
    {
        var existing = await _paymentRepository.GetCustomerByUserIdAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing.StripeCustomerId;
        }

        var customerService = new CustomerService();
        var created = await customerService.CreateAsync(new CustomerCreateOptions
        {
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId
            }
        }, cancellationToken: cancellationToken);

        var customer = new PaymentCustomer
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StripeCustomerId = created.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            await _paymentRepository.AddCustomerAsync(customer, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return customer.StripeCustomerId;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent Stripe customer mapping creation detected for UserId={UserId}", userId);
            var mapped = await _paymentRepository.GetCustomerByUserIdAsync(userId, cancellationToken);
            if (mapped is not null)
            {
                return mapped.StripeCustomerId;
            }

            throw;
        }
    }
}
