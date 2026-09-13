using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stripe;

namespace EShop.Payment.UnitTests.Services;

/// <summary>
/// Payment audit Stage 9 (M4). <c>AddPaymentInfrastructure</c> registers one Stripe client for the process, and the
/// Stripe services still resolve when no key is configured. That is the default with Stripe off, and the refunder needs
/// the Stripe service even to refund a simulated payment.
/// </summary>
[TestFixture]
public class StripeClientRegistrationTests
{
    private static ServiceProvider Build(string secretKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:SecretKey"] = secretKey })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentInfrastructure(configuration, useInMemoryDatabase: true);
        return services.BuildServiceProvider();
    }

    [Test]
    public void OneClient_ServesTheWholeProcess_WithTheConfiguredKey()
    {
        using var provider = Build("sk_test_registration");
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var client = first.ServiceProvider.GetRequiredService<IStripeClient>();

        Assert.Multiple(() =>
        {
            Assert.That(second.ServiceProvider.GetRequiredService<IStripeClient>(), Is.SameAs(client));
            Assert.That(client.ApiKey, Is.EqualTo("sk_test_registration"));
        });
    }

    /// <summary>
    /// The tracked appsettings.json ships <c>"SecretKey": ""</c>, and Stripe.net throws on an empty or whitespace key. So
    /// passing the setting through unchanged would fail every resolution of the Stripe services with Stripe off.
    /// </summary>
    [TestCase("")]
    [TestCase("   ")]
    public void WithNoKeyConfigured_TheStripeServicesStillResolve(string secretKey)
    {
        using var provider = Build(secretKey);
        using var scope = provider.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(() => scope.ServiceProvider.GetRequiredService<IStripePaymentService>(), Throws.Nothing);
            Assert.That(() => scope.ServiceProvider.GetRequiredService<IStripeCustomerService>(), Throws.Nothing);
            Assert.That(scope.ServiceProvider.GetRequiredService<IStripeClient>().ApiKey, Is.Null);
        });
    }
}
