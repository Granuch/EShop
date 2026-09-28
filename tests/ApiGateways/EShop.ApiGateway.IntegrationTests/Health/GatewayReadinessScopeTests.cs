using System.Net;
using EShop.ApiGateway.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.IntegrationTests.Health;

/// <summary>
/// docker-ci DC-45. The gateway's readiness used to be the AND of every service behind it plus its mail server:
/// stopping one database, or Mailpit, turned <c>/health/ready</c> into a 503, and any orchestrator routing on
/// readiness would have taken every gateway instance out of rotation while the other routes still worked. Readiness
/// now covers only what makes this instance unable to serve (its own email queue); <c>downstream</c> and <c>smtp</c>
/// stay on <c>/health</c>.
///
/// <para>This host is a plain <see cref="WebApplicationFactory{TEntryPoint}"/>, not <c>GatewayApiFactory</c>, because that
/// fixture removes the real downstream check: here the production registrations are what is tested. The configured
/// clusters point at compose host names that do not resolve outside the compose network, so the downstream check is
/// really Unhealthy; the test asserts that first, or the readiness assertion would prove nothing.</para>
/// </summary>
[TestFixture]
public sealed class GatewayReadinessScopeTests
{
    [Test]
    public void OnlyTheGatewaysOwnChecks_AreTaggedReady()
    {
        using var factory = new ProductionRegistrationsFactory();
        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var ready = registrations.Where(r => r.Tags.Contains("ready")).Select(r => r.Name).ToArray();

        Assert.That(ready, Is.EquivalentTo(new[] { "email-queue" }));
        Assert.That(registrations.Select(r => r.Name), Does.Contain(DownstreamHealthCheck.Name).And.Contain("smtp"),
            "both checks must still exist, on /health");
    }

    [Test]
    public async Task AnUnreachableDownstream_ShowsOnHealth_ButLeavesTheGatewayReady()
    {
        using var factory = new ProductionRegistrationsFactory();
        using var client = factory.CreateClient();

        var all = await client.GetAsync("/health");
        var allBody = await all.Content.ReadAsStringAsync();
        var ready = await client.GetAsync("/health/ready");
        var readyBody = await ready.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(all.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable),
                "precondition: the downstream check must really be failing, or this test proves nothing");
            Assert.That(allBody, Does.Contain(DownstreamHealthCheck.Name));
            Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(readyBody, Does.Not.Contain(DownstreamHealthCheck.Name).And.Not.Contain("smtp"));
        });
    }

    private sealed class ProductionRegistrationsFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("JwtSettings:SecretKey", "TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!");
            builder.UseSetting("JwtSettings:Issuer", "EShop.Identity");
            builder.UseSetting("JwtSettings:Audience", "EShop.Services");
        }
    }
}
