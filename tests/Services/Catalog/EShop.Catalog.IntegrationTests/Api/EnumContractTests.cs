using System.Net;
using System.Text.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Api;

/// <summary>
/// Frontend-contracts F-01. A product's <c>status</c> was an integer (<c>1</c> for Active) while every other service
/// sent names, and the <c>status</c>/<c>sortBy</c> filters took an exact-case name or a number. Both are now names: the
/// response sends PascalCase, and the filters take a name in any case and refuse a number with a
/// <c>ValidationError</c> rather than binding it.
/// </summary>
/// <remarks>
/// These read the raw JSON on purpose. The response models used by the rest of the suite already refuse an integer
/// (<c>Models/EnumNameConverter</c>), but a test that owns the claim should not depend on a model's attribute.
/// </remarks>
[TestFixture]
[Category("Integration")]
public class EnumContractTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    private async Task<(Guid Id, string Token)> SeedAsync(bool publish)
    {
        var token = $"EN{Guid.NewGuid():N}";
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var id = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, $"{token} product", CatalogDataHelper.GenerateUniqueSku("EN"), 10m, 5, categoryId, publish);
        return (id, token);
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    [Test]
    public async Task TheList_SendsTheStatusAsItsName()
    {
        var (id, token) = await SeedAsync(publish: true);

        var page = await GetJsonAsync($"{ProductsEndpoint}?searchTerm={token}");

        var item = page.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        item.GetProperty("status").GetString().Should().Be("Active");
    }

    [Test]
    public async Task TheDetail_SendsTheStatusAsItsName()
    {
        var (id, _) = await SeedAsync(publish: false);

        var product = await GetJsonAsync($"{ProductsEndpoint}/{id}");

        product.GetProperty("status").GetString().Should().Be("Draft");
    }

    [TestCase("draft")]
    [TestCase("DRAFT")]
    [TestCase("Draft")]
    public async Task TheStatusFilter_TakesTheNameInAnyCase(string status)
    {
        var (draftId, token) = await SeedAsync(publish: false);
        var (activeId, _) = await SeedAsync(publish: true);

        var page = await GetJsonAsync($"{ProductsEndpoint}?pageSize=100&searchTerm={token}&status={status}");

        var ids = page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        ids.Should().Contain(draftId).And.NotContain(activeId);
    }

    [Test]
    public async Task TheSortFilter_TakesTheNameInAnyCase()
    {
        using var response = await Client.GetAsync($"{ProductsEndpoint}?sortBy=price&isDescending=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [TestCase("status", "0")]
    [TestCase("status", "1")]
    [TestCase("status", "Bogus")]
    [TestCase("sortBy", "2")]
    [TestCase("sortBy", "Bogus")]
    public async Task ANumberOrAnUnknownName_IsAValidationError_KeyedByTheParameter(string parameter, string value)
    {
        using var response = await Client.GetAsync($"{ProductsEndpoint}?{parameter}={value}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("ValidationError");
        json.RootElement.GetProperty("errors").TryGetProperty(parameter, out _).Should().BeTrue(body);
    }

    /// <summary>The export shares the list's filter surface, so it refuses a number the same way.</summary>
    [Test]
    public async Task TheExport_RefusesANumberToo()
    {
        using var response = await Client.GetAsync($"{ProductsEndpoint}/export?status=1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }
}
