using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;

namespace EShop.Basket.IntegrationTests.Api;

/// <summary>
/// Admin panel S14. With no first-party client being written alongside the admin endpoints, the OpenAPI document is
/// the only description of them a UI developer gets (plan §9, "No frontend track"), so each must declare its response
/// shape and its query parameters rather than an untyped 200.
/// </summary>
[TestFixture]
[Category("Integration")]
public class AdminApiContractTests
{
    [TestCase("/api/v1/basket/admin/carts", "AdminBasketPageDto", new[] { "cursor", "pageSize" })]
    [TestCase("/api/v1/basket/admin/abandoned", "AdminBasketPageDto", new[] { "cursor", "pageSize", "olderThan" })]
    [TestCase("/api/v1/basket/admin/outbox/dead-letters/details", "OutboxDeadLetterPageDto", new[] { "offset", "limit" })]
    public async Task TheDocument_DescribesEachAdminRead_WithItsShapeAndParameters(string path, string schema, string[] parameters)
    {
        using var factory = new BasketApiFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var get = document.GetProperty("paths").GetProperty(path).GetProperty("get");

        get.GetProperty("responses").GetProperty("200").GetRawText().Should().Contain($"#/components/schemas/{schema}");
        get.GetProperty("responses").TryGetProperty("503", out _).Should().BeTrue("Redis being down is a declared 503");
        // Named as the record's properties are (PascalCase), as in every other service's [AsParameters] query; binding
        // is case-insensitive, so ?pageSize= and ?PageSize= are the same parameter.
        get.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()!.ToLowerInvariant())
            .Should().BeEquivalentTo(parameters.Select(p => p.ToLowerInvariant()));
    }
}
