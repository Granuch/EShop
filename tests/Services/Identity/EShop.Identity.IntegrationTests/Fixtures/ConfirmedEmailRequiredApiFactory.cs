using Microsoft.AspNetCore.Hosting;

namespace EShop.Identity.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres-backed host with <c>SignIn.RequireConfirmedEmail</c> on, as Production and (since the
/// email-confirmation feature landed) Sandbox run it. Every other fixture runs under Testing, where
/// confirmation is not required, so this is the only host on which an unconfirmed login is refused.
/// </summary>
public class ConfirmedEmailRequiredApiFactory : PostgresIdentityApiFactory
{
    private ConfirmedEmailRequiredApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static async Task<ConfirmedEmailRequiredApiFactory> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync(cancellationToken);
        return new ConfirmedEmailRequiredApiFactory(connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Host configuration, not ConfigureAppConfiguration: the Identity options are composed from
        // it while Program.cs builds the app, before an app-configuration source would apply.
        builder.UseSetting("Identity:RequireConfirmedEmail", "true");
        base.ConfigureWebHost(builder);
    }
}
