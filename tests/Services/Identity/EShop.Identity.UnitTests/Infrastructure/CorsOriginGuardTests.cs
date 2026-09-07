using EShop.BuildingBlocks.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace EShop.Identity.UnitTests.Infrastructure;

/// <summary>
/// SEC-08 (CORS half). The old guard asked only "is Cors:AllowedOrigins empty?", and the
/// tracked appsettings.Production.json of both Identity and Basket ships
/// ["https://your-production-frontend.com"] — a non-empty array. So a deploy that had never
/// configured CORS passed the check and silently approved a placeholder origin.
///
/// The guard also used to live inside the AddPolicy lambda, which CORS builds lazily on first
/// use; it is now called while composing the host so a misconfiguration fails at startup
/// rather than as a 500 on the first cross-origin request.
/// </summary>
[TestFixture]
public class CorsOriginGuardTests
{
    private static IConfiguration ConfigurationWith(params string[] origins)
    {
        var values = origins
            .Select((origin, index) => new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{index}", origin));

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(name);
        return environment.Object;
    }

    [Test]
    public void Production_WithThePlaceholderOriginFromTrackedConfig_Throws()
    {
        var configuration = ConfigurationWith("https://your-production-frontend.com");

        var thrown = Assert.Throws<InvalidOperationException>(
            () => CorsOriginGuard.GetValidatedOrigins(configuration, Environment("Production")));

        Assert.That(thrown!.Message, Does.Contain("your-production-frontend.com"));
    }

    [Test]
    public void Production_WithAnUnsubstitutedTokenOrigin_Throws()
    {
        var configuration = ConfigurationWith("https://#{FRONTEND_HOST}#");

        Assert.Throws<InvalidOperationException>(
            () => CorsOriginGuard.GetValidatedOrigins(configuration, Environment("Production")));
    }

    [Test]
    public void Production_WithOneRealAndOnePlaceholderOrigin_StillThrows()
    {
        // A partially-configured list is the realistic mistake: someone adds the real origin
        // and leaves the template entry behind.
        var configuration = ConfigurationWith("https://shop.example-real.test", "https://your-production-frontend.com");

        Assert.Throws<InvalidOperationException>(
            () => CorsOriginGuard.GetValidatedOrigins(configuration, Environment("Production")));
    }

    [Test]
    public void Production_WithNoOrigins_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => CorsOriginGuard.GetValidatedOrigins(ConfigurationWith(), Environment("Production")));
    }

    [Test]
    public void Production_WithRealOrigins_ReturnsThem()
    {
        var configuration = ConfigurationWith("https://shop.contoso-real.test");

        var origins = CorsOriginGuard.GetValidatedOrigins(configuration, Environment("Production"));

        Assert.That(origins, Is.EqualTo(new[] { "https://shop.contoso-real.test" }));
    }

    [Test]
    public void Sandbox_IsNotExempt()
    {
        // Sandbox is a deployed environment reachable from outside the developer's machine, so
        // it is held to the same standard — only Development and Testing are exempt.
        var configuration = ConfigurationWith("https://your-production-frontend.com");

        Assert.Throws<InvalidOperationException>(
            () => CorsOriginGuard.GetValidatedOrigins(configuration, Environment("Sandbox")));
    }

    [TestCase("Development")]
    [TestCase("Testing")]
    public void DevelopmentAndTesting_TolerateNoOrigins(string environmentName)
    {
        var origins = CorsOriginGuard.GetValidatedOrigins(ConfigurationWith(), Environment(environmentName));

        Assert.That(origins, Is.Empty);
    }
}
