using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EShop.Notification.IntegrationTests.Fixtures;

/// <summary>
/// The host as Testing (EF InMemory, no bus), given the settings <c>NotificationConfigurationGuard</c> requires in every
/// environment (Notification audit S4). They go through <c>UseSetting</c>: <c>Program.cs</c> reads them while composing
/// the host, where <c>ConfigureAppConfiguration</c> would land too late.
/// </summary>
public class NotificationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Smtp:Host", "smtp.invalid");
        builder.UseSetting("IdentityService:BaseUrl", "http://identity.invalid/");
        builder.UseSetting("PasswordReset:ResetUrlBase", "https://shop.eshop-real.test/reset-password");
    }
}
