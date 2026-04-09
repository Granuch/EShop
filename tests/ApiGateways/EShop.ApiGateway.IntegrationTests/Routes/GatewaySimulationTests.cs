using EShop.ApiGateway.IntegrationTests.Fixtures;

namespace EShop.ApiGateway.IntegrationTests.Routes;

[TestFixture]
public sealed class GatewaySimulationTests
{
    [Test]
    public async Task Request_ToSimulationRoute_ReturnsSimulatedResponse()
    {
        await using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/orders");
        request.Headers.Add("X-Simulate", "true");

        var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        Assert.That(payload, Does.Contain("items"));
        Assert.That(factory.NotificationCollector.Items.Any(x => x.EventType == "SimulationResponse"), Is.True);
    }

    [Test]
    public async Task Request_WithSimulationHeaderDisabled_AttemptsProxyAndReturnsBadGateway()
    {
        await using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/orders");
        request.Headers.Add("X-Simulate", "false");

        var response = await client.SendAsync(request);

        Assert.That((int)response.StatusCode, Is.EqualTo(502));
    }

    [Test]
    public async Task Request_ToForcedSimulationFailure_QueuesFailureNotification()
    {
        await using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/failure/orders");
        request.Headers.Add("X-Simulate", "true");

        var response = await client.SendAsync(request);

        Assert.That((int)response.StatusCode, Is.EqualTo(503));
        Assert.That(factory.NotificationCollector.Items.Any(x => x.EventType == "SimulationFailureTriggered"), Is.True);
    }

    [Test]
    public async Task SecondRequest_FromSameHost_TriggersRateLimitNotification()
    {
        await using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        using var first = new HttpRequestMessage(HttpMethod.Get, "/test/orders");
        first.Headers.Add("X-Simulate", "false");
        _ = await client.SendAsync(first);

        using var second = new HttpRequestMessage(HttpMethod.Get, "/test/orders");
        second.Headers.Add("X-Simulate", "false");
        var secondResponse = await client.SendAsync(second);

        Assert.That((int)secondResponse.StatusCode, Is.EqualTo(429));
        Assert.That(factory.NotificationCollector.Items.Any(x => x.EventType == "RateLimitExceeded"), Is.True);
    }
}
