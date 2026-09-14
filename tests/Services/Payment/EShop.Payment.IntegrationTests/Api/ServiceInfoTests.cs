using System.Net;
using System.Text.Json;

namespace EShop.Payment.IntegrationTests.Api;

/// <summary>
/// Payment audit Stage 12 (D17). <c>GET /</c> is anonymous. It used to name the environment that answered, which told
/// any caller whether it had reached Production or Sandbox.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ServiceInfoTests : IntegrationTestBase
{
    [Test]
    public async Task TheAnonymousInfoPage_DoesNotNameTheEnvironment()
    {
        var response = await Client.GetAsync("/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.TryGetProperty("environment", out _), Is.False);
            Assert.That(body.RootElement.GetProperty("service").GetString(), Is.EqualTo("EShop Payment API"));
        });
    }
}
