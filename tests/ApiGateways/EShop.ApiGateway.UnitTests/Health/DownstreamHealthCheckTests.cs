using System.Net;
using EShop.ApiGateway.Health;
using Microsoft.Extensions.Primitives;
using Moq;
using Moq.Protected;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.UnitTests.Health;

[TestFixture]
public sealed class DownstreamHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_ShouldReturnUnhealthy_WhenNoRoutesConfigured()
    {
        var proxyProvider = new Mock<IProxyConfigProvider>();
        proxyProvider.Setup(x => x.GetConfig()).Returns(new TestProxyConfig(Array.Empty<RouteConfig>(),
            [
                new ClusterConfig
                {
                    ClusterId = "identity-cluster",
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["d1"] = new() { Address = "http://identity-api:8080/" }
                    }
                }
            ]));

        var clientFactory = CreateHttpClientFactory(HttpStatusCode.OK);
        var sut = new DownstreamHealthCheck(proxyProvider.Object, clientFactory.Object);

        var result = await sut.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy));
        Assert.That(result.Description, Is.EqualTo("No YARP routes configured."));
    }

    [Test]
    public async Task CheckHealthAsync_ShouldReturnUnhealthy_WhenNoClustersConfigured()
    {
        var proxyProvider = new Mock<IProxyConfigProvider>();
        proxyProvider.Setup(x => x.GetConfig()).Returns(new TestProxyConfig(
            [
                new RouteConfig
                {
                    RouteId = "identity-route",
                    ClusterId = "identity-cluster",
                    Match = new RouteMatch { Path = "/api/v1/auth/{**catch-all}" }
                }
            ],
            Array.Empty<ClusterConfig>()));

        var clientFactory = CreateHttpClientFactory(HttpStatusCode.OK);
        var sut = new DownstreamHealthCheck(proxyProvider.Object, clientFactory.Object);

        var result = await sut.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy));
        Assert.That(result.Description, Is.EqualTo("No YARP clusters configured."));
    }

    [Test]
    public async Task CheckHealthAsync_ShouldReturnUnhealthy_WhenDestinationReturnsFailureStatus()
    {
        var proxyProvider = new Mock<IProxyConfigProvider>();
        proxyProvider.Setup(x => x.GetConfig()).Returns(CreateValidConfig("http://identity-api:8080/"));

        var clientFactory = CreateHttpClientFactory(HttpStatusCode.ServiceUnavailable);
        var sut = new DownstreamHealthCheck(proxyProvider.Object, clientFactory.Object);

        var result = await sut.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy));
        Assert.That(result.Data.ContainsKey("failedDestinations"), Is.True);
    }

    [Test]
    public async Task CheckHealthAsync_ShouldReturnHealthy_WhenAllDestinationsReady()
    {
        var proxyProvider = new Mock<IProxyConfigProvider>();
        proxyProvider.Setup(x => x.GetConfig()).Returns(CreateValidConfig("http://identity-api:8080/"));

        var clientFactory = CreateHttpClientFactory(HttpStatusCode.OK);
        var sut = new DownstreamHealthCheck(proxyProvider.Object, clientFactory.Object);

        var result = await sut.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy));
        Assert.That(result.Description, Is.EqualTo("All downstream destinations are reachable and healthy."));
    }

    private static TestProxyConfig CreateValidConfig(string destinationAddress)
    {
        return new TestProxyConfig(
            [
                new RouteConfig
                {
                    RouteId = "identity-route",
                    ClusterId = "identity-cluster",
                    Match = new RouteMatch { Path = "/api/v1/auth/{**catch-all}" }
                }
            ],
            [
                new ClusterConfig
                {
                    ClusterId = "identity-cluster",
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["d1"] = new() { Address = destinationAddress }
                    }
                }
            ]);
    }

    private static Mock<IHttpClientFactory> CreateHttpClientFactory(HttpStatusCode statusCode)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));

        var clientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        clientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));

        return clientFactory;
    }

    private sealed class TestProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters) : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public IChangeToken ChangeToken { get; } = new TestChangeToken();
    }

    private sealed class TestChangeToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
