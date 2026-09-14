using System.Net;

namespace EShop.Payment.IntegrationTests.Api;

/// <summary>
/// Ordering audit L10 / D15: every service serves its API docs in every environment except Production,
/// through the shared <c>EShopApiDocs</c> rule. Payment used to serve its OpenAPI document in Development only.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ApiDocsExposureTests : IntegrationTestBase
{
    [Test]
    public async Task TheOpenApiDocument_IsServedOutsideProduction()
    {
        Assert.That((await Client.GetAsync("/openapi/v1.json")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
