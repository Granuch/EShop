using System.Net;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EShop.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// SEC-07. <c>/prometheus</c> and <c>/metrics</c> were anonymous and unfiltered in all seven
/// components. The metrics themselves are not secrets, but together they disclose route
/// inventory, traffic volumes, error rates and runtime detail — reconnaissance material with no
/// reason to be reachable from outside the deployment.
///
/// The gate defaults to loopback + private ranges so it cannot break a working scrape: the
/// compose bridge (172.16/12), a Kubernetes pod CIDR (10/8) and localhost all still reach it.
/// What it stops is a published port or a too-broad ingress answering the public internet.
/// </summary>
[TestFixture]
public class MetricsAccessTests
{
    private static async Task<HttpResponseMessage> RequestAsync(
        string path,
        string? remoteIp,
        string environmentName = "Production",
        params string[] allowedNetworks)
    {
        var settings = allowedNetworks
            .Select((cidr, index) => new KeyValuePair<string, string?>($"Metrics:AllowedNetworks:{index}", cidr));

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .UseEnvironment(environmentName)
                    .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
                    .Configure((context, app) =>
                    {
                        // TestServer leaves RemoteIpAddress null, so stand in for the socket the
                        // way RateLimitingApiFactory does for the rate limiters.
                        app.Use(async (httpContext, next) =>
                        {
                            httpContext.Connection.RemoteIpAddress =
                                remoteIp is null ? null : IPAddress.Parse(remoteIp);
                            await next();
                        });

                        app.UseEShopMetricsAccess(context.Configuration, context.HostingEnvironment);

                        app.Run(httpContext =>
                        {
                            httpContext.Response.StatusCode = StatusCodes.Status200OK;
                            return httpContext.Response.WriteAsync("# HELP scraped");
                        });
                    });
            })
            .StartAsync();

        return await host.GetTestClient().GetAsync(path);
    }

    [TestCase("/prometheus")]
    [TestCase("/metrics")]
    public async Task PublicAddress_IsRejected(string path)
    {
        var response = await RequestAsync(path, "203.0.113.10");

        // 404, not 403 — a 403 confirms the endpoint is there.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase("127.0.0.1", TestName = "Loopback")]
    [TestCase("10.42.0.7", TestName = "KubernetesPodCidr")]
    [TestCase("172.18.0.5", TestName = "DockerComposeBridge")]
    [TestCase("192.168.1.20", TestName = "PrivateLan")]
    public async Task InternalAddress_IsAllowedByDefault(string remoteIp)
    {
        var response = await RequestAsync("/prometheus", remoteIp);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task IPv4MappedIPv6_IsNormalisedBeforeMatching()
    {
        // ::ffff:203.0.113.10 must not slip past the private-range check by arriving as IPv6.
        var response = await RequestAsync("/prometheus", "::ffff:203.0.113.10");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ConfiguredNetworks_ReplaceTheDefaults()
    {
        var allowed = await RequestAsync("/prometheus", "203.0.113.10", "Production", "203.0.113.0/24");
        Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Once Metrics:AllowedNetworks is set it is the whole allow-list, so an address that
        // the defaults would have permitted is now rejected. That is the point of configuring
        // it, but it is also the way to lock yourself out of your own scrape.
        var rejected = await RequestAsync("/prometheus", "10.42.0.7", "Production", "203.0.113.0/24");
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task NonMetricsPaths_AreNotAffected()
    {
        var response = await RequestAsync("/health/ready", "203.0.113.10");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Testing_IsExemptEntirely()
    {
        // The integration suites scrape /metrics and /prometheus through TestServer, which has
        // no socket at all; without this exemption every one of those tests would 404.
        var response = await RequestAsync("/metrics", "203.0.113.10", "Testing");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
