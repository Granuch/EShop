using System.Net;
using EShop.ApiGateway.IntegrationTests.Fixtures;

namespace EShop.ApiGateway.IntegrationTests.Api;

/// <summary>
/// Ordering audit L10 / D15: every component serves its API docs in every environment except Production,
/// through the shared <c>EShopApiDocs</c> rule. The gateway used to serve its OpenAPI document in
/// Development only.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ApiDocsExposureTests
{
    [Test]
    public async Task TheOpenApiDocument_IsServedOutsideProduction()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        Assert.That((await client.GetAsync("/openapi/v1.json")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
