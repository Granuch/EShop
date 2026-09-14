using System.Net;
using EShop.Basket.IntegrationTests.Fixtures;

namespace EShop.Basket.IntegrationTests.Api;

/// <summary>
/// Ordering audit L10 / D15: every service serves its API docs in every environment except Production,
/// through the shared <c>EShopApiDocs</c> rule. Basket used to serve them in Development only.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ApiDocsExposureTests
{
    [TestCase("/openapi/v1.json")]
    [TestCase("/scalar/v1")]
    public async Task TheApiDocs_AreServedOutsideProduction(string path)
    {
        using var factory = new BasketApiFactory();
        using var client = factory.CreateClient();

        Assert.That((await client.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
