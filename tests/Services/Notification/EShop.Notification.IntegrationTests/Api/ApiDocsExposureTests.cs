using System.Net;
using EShop.Notification.IntegrationTests.Fixtures;

namespace EShop.Notification.IntegrationTests.Api;

/// <summary>
/// Ordering audit L10 / D15: every service serves its API docs in every environment except Production, through the
/// shared <c>EShopApiDocs</c> rule. Notification is the last component to join, because until Admin panel S12 it had no
/// API to document — and with no first-party client being written for the admin panel, <c>/openapi/v1.json</c> is the
/// only thing describing the journal to whoever builds the UI.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ApiDocsExposureTests
{
    [TestCase("/openapi/v1.json")]
    [TestCase("/scalar/v1")]
    public async Task TheApiDocs_AreServedOutsideProduction(string path)
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();

        Assert.That((await client.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>
    /// The document must actually describe the journal, not merely exist. A <c>MapGroup</c> that was never mapped, or
    /// an endpoint without <c>.Produces&lt;T&gt;()</c>, still yields a perfectly valid document.
    /// </summary>
    [Test]
    public async Task TheDocument_DescribesTheJournalEndpoints_WithTheirResponseShapes()
    {
        await using var factory = new NotificationApiFactory();
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json");
        using var json = System.Text.Json.JsonDocument.Parse(document);
        var paths = json.RootElement.GetProperty("paths");

        Assert.Multiple(() =>
        {
            Assert.That(paths.TryGetProperty("/api/v1/notifications", out _), Is.True, document);
            Assert.That(paths.TryGetProperty("/api/v1/notifications/stats", out _), Is.True, document);
            Assert.That(paths.TryGetProperty("/api/v1/notifications/{id}", out _), Is.True, document);
        });

        var listOk = paths.GetProperty("/api/v1/notifications").GetProperty("get")
            .GetProperty("responses").GetProperty("200");

        Assert.That(listOk.GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _),
            Is.True,
            "the 200 response must carry a schema — Ordering audit L8 had to retrofit this once, when every endpoint "
            + "declared Produces<object> and the document described no response shapes at all");
    }
}
