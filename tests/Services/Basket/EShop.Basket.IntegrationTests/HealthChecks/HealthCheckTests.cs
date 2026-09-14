using System.Text.Json;
using EShop.Basket.IntegrationTests.Fixtures;

namespace EShop.Basket.IntegrationTests.HealthChecks;

[TestFixture]
public class HealthCheckTests
{
    [Test]
    public async Task LiveEndpoint_ShouldReturnSuccess()
    {
        await using var factory = new BasketApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.That(response.IsSuccessStatusCode, Is.True);
    }

    /// <summary>
    /// Basket audit S10 (L10): the anonymous root names the service, and neither the environment it runs in nor its
    /// routes.
    /// </summary>
    [Test]
    public async Task RootEndpoint_ShouldReturnServiceInfo_WithoutTheEnvironmentOrTheRoutes()
    {
        await using var factory = new BasketApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.That(response.IsSuccessStatusCode, Is.True);
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;
        Assert.That(root.GetProperty("service").GetString(), Is.EqualTo("EShop Basket API"));
        Assert.That(root.TryGetProperty("environment", out _), Is.False, body);
        Assert.That(body, Does.Not.Contain("/api/v1/basket"), "the route map belongs in the OpenAPI document");
    }
}
