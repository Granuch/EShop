using System.Net;

namespace EShop.Catalog.IntegrationTests.Api;

/// <summary>
/// Ordering audit L10 / D15: every service serves its API docs in every environment except Production,
/// through the shared <c>EShopApiDocs</c> rule. Catalog used to exclude Testing (and serve Production).
/// </summary>
[TestFixture]
[Category("Integration")]
public class ApiDocsExposureTests : IntegrationTestBase
{
    [TestCase("/openapi/v1.json")]
    [TestCase("/scalar/v1")]
    public async Task TheApiDocs_AreServedOutsideProduction(string path)
    {
        using var anonymous = Factory.CreateClient();

        Assert.That((await anonymous.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
