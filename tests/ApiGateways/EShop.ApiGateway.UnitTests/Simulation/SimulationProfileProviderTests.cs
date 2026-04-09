using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Simulation;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Simulation;

[TestFixture]
public class SimulationProfileProviderTests
{
    [Test]
    public void TryGetByPath_ReturnsLongestPrefixMatch()
    {
        var options = Options.Create(new SimulationOptions
        {
            Routes = new Dictionary<string, SimulationRouteOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["orders"] = new() { PathPrefix = "/api/v1/orders", DelayMs = new DelayRangeOptions { Min = 10, Max = 20 }, ErrorRate = 0.1 },
                ["orders-items"] = new() { PathPrefix = "/api/v1/orders/items", DelayMs = new DelayRangeOptions { Min = 1, Max = 2 }, ErrorRate = 0.05 }
            }
        });

        var provider = new SimulationProfileProvider(options);

        var found = provider.TryGetByPath("/api/v1/orders/items/42", out var profile);

        Assert.That(found, Is.True);
        Assert.That(profile.RouteId, Is.EqualTo("orders-items"));
    }

    [Test]
    public void TryGetByPath_ReturnsFalseWhenNoMatch()
    {
        var options = Options.Create(new SimulationOptions
        {
            Routes = new Dictionary<string, SimulationRouteOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["orders"] = new() { PathPrefix = "/api/v1/orders", DelayMs = new DelayRangeOptions { Min = 10, Max = 20 } }
            }
        });

        var provider = new SimulationProfileProvider(options);

        var found = provider.TryGetByPath("/api/v1/products", out _);

        Assert.That(found, Is.False);
    }
}
