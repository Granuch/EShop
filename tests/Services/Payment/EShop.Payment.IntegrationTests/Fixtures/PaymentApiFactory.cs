using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Fixtures;

/// <summary>
/// The Payment host under Testing.
/// <para>Payment audit Stage 11. Settings arrive through <c>UseSetting</c>, which <c>Program.cs</c> sees while it composes
/// the host. They used to come through <c>ConfigureAppConfiguration</c>, which is applied only when the host is built,
/// after those reads. That had two effects:</para>
/// <list type="bullet">
///   <item><c>Program.cs</c> checked and used the tracked <c>appsettings.json</c> placeholder JWT key. A
///   <c>PostConfigure&lt;JwtBearerOptions&gt;</c> then swapped the test key in.</item>
///   <item>The Stripe flags never reached the startup bypass check.</item>
/// </list>
/// <para>Both workarounds are gone. <c>Program.cs</c>'s own JWT setup now validates the tokens the tests sign.</para>
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    internal const string TestJwtSecretKey = "THIS_IS_A_TEST_ONLY_SECRET_KEY_32_CHARS_MINIMUM";
    internal const string TestJwtIssuer = "ESHOP_PAYMENT_TEST_ISSUER";
    internal const string TestJwtAudience = "ESHOP_PAYMENT_TEST_AUDIENCE";

    internal static readonly IReadOnlyDictionary<string, string?> Settings = new Dictionary<string, string?>
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
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
        PaymentMethodType method = PaymentMethodType.Stripe,
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
