using EShop.Payment.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Ordering audit Stage 19 (D16): automatic refunds of cancelled orders are built but off, and manual review
/// stays the default until the owner turns them on. Pins the shipped default and the configuration key that
/// <c>docker-compose.yml</c> sets (<c>CancelledOrders__AutoRefund</c>).
/// </summary>
[TestFixture]
[Category("Integration")]
public class CancelledOrderRefundSettingsTests : IntegrationTestBase
{
    [Test]
    public void AutomaticRefunds_AreOff_UnlessConfigured()
    {
        var settings = Factory.Services.GetRequiredService<IOptions<CancelledOrderRefundSettings>>().Value;

        Assert.That(settings.AutoRefund, Is.False);
    }

    [Test]
    public void AutomaticRefunds_AreTurnedOn_ByTheCancelledOrdersAutoRefundKey()
    {
        using var configured = Factory.WithWebHostBuilder(b => b.UseSetting("CancelledOrders:AutoRefund", "true"));

        var settings = configured.Services.GetRequiredService<IOptions<CancelledOrderRefundSettings>>().Value;

        Assert.That(settings.AutoRefund, Is.True);
    }
}
