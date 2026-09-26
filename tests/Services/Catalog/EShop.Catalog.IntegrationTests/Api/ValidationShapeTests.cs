using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Api;

/// <summary>
/// Frontend-contracts F-03. A validation failure reaches the client by two routes: returned as a Result (queries and
/// commands with a value) or thrown (commands returning the non-generic Result). They used to answer two shapes with
/// two codes; both now answer 400 <c>ValidationError</c> with a camelCase <c>errors</c> map and the messages in
/// <c>detail</c>.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ValidationShapeTests : AuthenticatedIntegrationTestBase
{
    [Test]
    public async Task AReturnedFailure_HasTheFieldMap()
    {
        using var response = await Client.GetAsync("/api/v1/products?pageSize=101");

        var problem = await AssertValidationShapeAsync(response);
        problem.GetProperty("errors").GetProperty("pageSize").EnumerateArray().Should().NotBeEmpty();
    }

    [Test]
    public async Task AThrownFailure_HasTheSameShape()
    {
        Guid categoryId;
        using (var scope = Factory.Services.CreateScope())
        {
            categoryId = await CatalogDataHelper.CreateCategoryAsync(
                scope.ServiceProvider, "fe-r2 shape", $"fe-r2-shape-{Guid.NewGuid():N}");
        }

        using var response = await Client.PutAsJsonAsync($"/api/v1/categories/{categoryId}", new { id = categoryId, name = "" });

        var problem = await AssertValidationShapeAsync(response);
        problem.GetProperty("errors").GetProperty("name").EnumerateArray().Should().NotBeEmpty();
    }

    [Test]
    public async Task ARuleAboutTheWholeRequest_IsKeyedDollar()
    {
        Guid categoryId;
        using (var scope = Factory.Services.CreateScope())
        {
            categoryId = await CatalogDataHelper.CreateCategoryAsync(
                scope.ServiceProvider, "fe-r2 self", $"fe-r2-self-{Guid.NewGuid():N}");
        }

        using var response = await Client.PutAsJsonAsync(
            $"/api/v1/categories/{categoryId}/parent", new { newParentCategoryId = categoryId });

        var problem = await AssertValidationShapeAsync(response);
        problem.GetProperty("errors").TryGetProperty("$", out _).Should().BeTrue("a category cannot be its own parent");
    }

    private static async Task<JsonElement> AssertValidationShapeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement.Clone();
        root.GetProperty("errorCode").GetString().Should().Be("ValidationError");
        root.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
        foreach (var key in root.GetProperty("errors").EnumerateObject().Select(p => p.Name))
        {
            (key == "$" || char.IsLower(key[0])).Should().BeTrue($"'{key}' must be a camelCase wire name or '$'");
        }

        var detail = root.GetProperty("detail").GetString();
        detail.Should().NotBe("One or more validation errors occurred.", "detail carries the messages");
        detail.Should().NotMatchRegex("^[A-Z][A-Za-z]*: ", "detail has no property prefix");
        return root;
    }
}
