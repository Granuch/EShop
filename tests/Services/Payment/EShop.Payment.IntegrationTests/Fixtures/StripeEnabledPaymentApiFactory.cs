using EShop.Payment.Application.Payments.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace EShop.Payment.IntegrationTests.Fixtures;

/// <summary>
/// The Payment host with <c>Stripe:Enabled</c> on and both Stripe services replaced by mocks, so the Stripe
/// endpoints run end to end over HTTP without reaching Stripe (Payment audit Stage 2). Replacing the services also
/// keeps the real <c>StripePaymentService</c> from being built, which would overwrite the process-wide
/// <c>StripeConfiguration.ApiKey</c> that <c>StripeSandboxTests</c> rely on.
/// <para>The webhook parser is left real (Stage 4): it needs only <see cref="WebhookSecret"/>, so the webhook tests
/// check genuine Stripe signatures.</para>
/// </summary>
public sealed class StripeEnabledPaymentApiFactory : PaymentApiFactory
{
    public const string WebhookSecret = "whsec_eshop_payment_integration_tests";

    private readonly bool _verifyWebhookSignatures;

    /// <param name="verifyWebhookSignatures">
    /// False turns on the Sandbox bypass (<c>SkipWebhookSignatureVerification</c> and
    /// <c>AllowMissingSignatureHeaderInBypassMode</c>).
    /// </param>
    public StripeEnabledPaymentApiFactory(bool verifyWebhookSignatures = true)
    {
        _verifyWebhookSignatures = verifyWebhookSignatures;
    }

    public Mock<IStripePaymentService> Stripe { get; } = new();

    public Mock<IStripeCustomerService> StripeCustomers { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        var bypass = _verifyWebhookSignatures ? "false" : "true";

        // Read through IOptions<StripeSettings> at request time, so ConfigureAppConfiguration is early enough here.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:Enabled"] = "true",
            ["Stripe:SecretKey"] = "sk_test_not_a_real_key",
            ["Stripe:WebhookSecret"] = WebhookSecret,
            ["Stripe:SkipWebhookSignatureVerification"] = bypass,
            ["Stripe:AllowMissingSignatureHeaderInBypassMode"] = bypass
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStripePaymentService>();
            services.AddSingleton(Stripe.Object);
            services.RemoveAll<IStripeCustomerService>();
            services.AddSingleton(StripeCustomers.Object);
        });
    }
}
