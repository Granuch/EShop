using System.Text;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace EShop.Payment.IntegrationTests.Fixtures;

public class PaymentApiFactory : WebApplicationFactory<Program>
{
    internal const string TestJwtSecretKey = "THIS_IS_A_TEST_ONLY_SECRET_KEY_32_CHARS_MINIMUM";
    internal const string TestJwtIssuer = "ESHOP_PAYMENT_TEST_ISSUER";
    internal const string TestJwtAudience = "ESHOP_PAYMENT_TEST_AUDIENCE";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = TestJwtSecretKey,
                ["JwtSettings:Issuer"] = TestJwtIssuer,
                ["JwtSettings:Audience"] = TestJwtAudience,
                ["RabbitMQ:Host"] = string.Empty,
                ["RabbitMQ:Username"] = string.Empty,
                ["RabbitMQ:Password"] = string.Empty,
                ["PaymentSimulation:ProcessingDelayMinSeconds"] = "0",
                ["PaymentSimulation:ProcessingDelayMaxSeconds"] = "0",
                ["PaymentSimulation:RefundDelaySeconds"] = "0",
                ["PaymentSimulation:SuccessRatePercent"] = "100",
                ["Stripe:Enabled"] = "false",
                ["Stripe:SkipWebhookSignatureVerification"] = "false",
                ["Stripe:AllowMissingSignatureHeaderInBypassMode"] = "false"
            };

            configBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = TestJwtIssuer,
                    ValidAudience = TestJwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecretKey)),
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };
            });

            services.RemoveAll<IPaymentProcessor>();
            services.AddScoped<IPaymentProcessor, MockPaymentProcessor>();
        });
    }

    /// <summary>
    /// Stores a payment directly. Every payment is recorded from <c>OrderCreatedEvent</c> since Payment audit Stage 1,
    /// and no endpoint creates one any more (Stage 3), so tests that need a payment put it in the database.
    /// </summary>
    public async Task SeedAsync(PaymentTransaction payment)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
    }

    /// <summary>Stores a payment for <paramref name="userId"/> with a new order id and returns it.</summary>
    public async Task<PaymentTransaction> SeedPaymentAsync(
        string userId,
        PaymentStatus status = PaymentStatus.Pending,
        decimal amount = 100m,
        string method = "Stripe",
        string intentId = "")
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = userId,
            Amount = amount,
            Currency = "USD",
            PaymentMethod = method,
            PaymentIntentId = intentId,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await SeedAsync(payment);
        return payment;
    }

    public async Task<PaymentTransaction?> FindByOrderIdAsync(Guid orderId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        return await db.PaymentTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId);
    }
}
