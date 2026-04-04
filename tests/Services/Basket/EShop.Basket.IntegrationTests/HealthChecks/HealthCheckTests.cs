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

    [Test]
    public async Task RootEndpoint_ShouldReturnServiceInfo()
    {
        await using var factory = new BasketApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.That(response.IsSuccessStatusCode, Is.True);
    }
}
