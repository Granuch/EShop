using Microsoft.AspNetCore.Hosting;

namespace EShop.Identity.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres-backed host in strict mode, <c>SignIn.RequireConfirmedEmail</c> on. Since soft email verification
/// nothing ships it (the code default, compose, k8s and <c>.env.example</c> are all off), but the switch still works,
/// and this is the only host on which an unconfirmed login is refused.
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
