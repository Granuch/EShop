using EShop.Identity.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;

namespace EShop.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Factory for exercising the <c>InternalService</c> authorization policy.
///
/// <para>
/// The tracked <c>appsettings.json</c> ships <c>InternalServiceAuth:ApiKey</c> as an empty string
/// and <c>appsettings.Testing.json</c> does not override it, so under Testing the policy's handler
/// short-circuits on the blank key and denies everything. That is safe, but it also means the
/// policy could never be tested without supplying a key — which is precisely why the whole
/// API-key surface had no coverage before Stage 8.
/// </para>
///
/// <para>
/// The key is delivered with <c>UseSetting</c>, not <c>ConfigureAppConfiguration</c>:
/// <c>Program.cs</c> binds <c>InternalServiceAuthSettings</c> and runs its placeholder guard while
/// composing the app, so a configuration source added the other way arrives after those reads and
/// the empty default silently wins.
/// </para>
/// </summary>
public class InternalServiceApiFactory : PostgresIdentityApiFactory
{
    public const string ConfiguredApiKey = "test-internal-service-key-8f2b1c";
    public const string HeaderName = "X-Internal-Api-Key";

    private readonly string _apiKey;

    private InternalServiceApiFactory(string connectionString, string apiKey) : base(connectionString)
    {
        _apiKey = apiKey;
    }

    /// <param name="apiKey">
    /// The key the server is configured with. Pass an empty string to reproduce the
    /// unconfigured-in-production case, where the handler must deny rather than allow.
    /// </param>
    public static async Task<InternalServiceApiFactory> CreateAsync(
        string apiKey = ConfiguredApiKey,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync(cancellationToken);
        return new InternalServiceApiFactory(connectionString, apiKey);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("InternalServiceAuth:ApiKey", _apiKey);
        builder.UseSetting("InternalServiceAuth:HeaderName", HeaderName);

        base.ConfigureWebHost(builder);
    }
}
