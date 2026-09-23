using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Security;

/// <summary>
/// M13 (Catalog audit Stage 9). A signed-in caller who is <b>not</b> an admin.
///
/// <para>
/// Until this fixture existed nothing in the suite could fail on authorization: every authenticated
/// test ran as <c>Admin</c> and <see cref="SecurityTests"/> covers only anonymous → 401. Downgrading
/// every <c>RequireAuthorization("Admin")</c> to <c>RequireAuthorization()</c> — any valid token
/// will do — left the whole suite green, because an admin passes both and an anonymous caller fails
/// both. Only a valid non-admin token tells them apart.
/// </para>
///
/// <para>
/// <see cref="AdminOnlyRoutes"/> is the single list both halves use: the behavioural test sends each
/// route and expects 403, and the structural test checks it against the endpoints the app actually
/// registered — so a new write endpoint cannot ship without either the Admin policy or a line here.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Security")]
public class NonAdminAuthorizationTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Customer";
    protected override string TestUserEmail => "customer@test.com";

    /// <summary>Every non-GET route under <c>/api/</c>, in <c>"METHOD pattern"</c> form as registered.</summary>
    private static readonly string[] AdminOnlyRoutes =
    [
        "POST /api/v1/products",
        "PUT /api/v1/products/{id:guid}",
        "DELETE /api/v1/products/{id:guid}",
        "POST /api/v1/products/{id:guid}/restore",
        "PATCH /api/v1/products/{id:guid}/stock",
        "POST /api/v1/products/{id:guid}/publish",
        "POST /api/v1/products/{id:guid}/unpublish",
        "PUT /api/v1/products/{id:guid}/discount",
        "DELETE /api/v1/products/{id:guid}/discount",
        "POST /api/v1/products/{id:guid}/images",
        "PUT /api/v1/products/{id:guid}/images/reorder",
        "PUT /api/v1/products/{id:guid}/images/{imageId:guid}",
        "DELETE /api/v1/products/{id:guid}/images/{imageId:guid}",
        "PUT /api/v1/products/{id:guid}/images/{imageId:guid}/main",
        "POST /api/v1/products/{id:guid}/attributes",
        "PUT /api/v1/products/{id:guid}/attributes",
        "PUT /api/v1/products/{id:guid}/attributes/{attributeId:guid}",
        "DELETE /api/v1/products/{id:guid}/attributes/{attributeId:guid}",
        // Admin panel S16.
        "POST /api/v1/products/bulk/publish",
        "POST /api/v1/products/bulk/unpublish",
        "POST /api/v1/products/bulk/delete",
        "POST /api/v1/products/bulk/category",
        "POST /api/v1/products/bulk/price",
        "POST /api/v1/products/import",
        "POST /api/v1/categories",
        "PUT /api/v1/categories/{id:guid}",
        "DELETE /api/v1/categories/{id:guid}",
        "PUT /api/v1/categories/reorder",
        "PUT /api/v1/categories/{id:guid}/parent",
        "POST /api/v1/categories/{id:guid}/restore",
    ];

    /// <summary>
    /// Admin panel S16. Every GET under <c>/api/</c>, with the policy it carries — <c>null</c> for a deliberately anonymous
    /// read. The write list above could not see an admin-only GET, and S16 added one under a prefix whose other reads are
    /// public (<c>/products/export</c>, beside S4's <c>/products/deleted</c>): downgrading either to anonymous would have
    /// left every structural check green. Ordering's, Payment's and Notification's structural tests already cover GETs;
    /// this brings Catalog's level with them.
    /// </summary>
    private static readonly Dictionary<string, string?> ReadPolicies = new(StringComparer.Ordinal)
    {
        ["GET /api/v1/products"] = null,
        ["GET /api/v1/products/newest"] = null,
        ["GET /api/v1/products/{id:guid}"] = null,
        ["GET /api/v1/products/deleted"] = "Admin",
        ["GET /api/v1/products/export"] = "Admin",
        ["GET /api/v1/categories"] = null,
        ["GET /api/v1/categories/{id:guid}"] = null,
        ["GET /api/v1/categories/{id:guid}/products"] = null,
        ["GET /api/v1/categories/{id:guid}/stats"] = "Admin",
        ["GET /api/v1/admin/catalog/low-stock"] = "Admin",
        ["GET /api/v1/admin/audit"] = "audit.read",
    };

    private Guid _categoryId;
    private Guid _productId;

    /// <summary>
    /// Real targets rather than random ids, so that if a route's policy were dropped the request
    /// would genuinely succeed instead of failing on a missing row — the failure then reads as the
    /// security hole it is, not as a 404.
    /// </summary>
    [OneTimeSetUp]
    public async Task SeedTargetsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        _categoryId = await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "Forbidden Target", $"forbidden-{Guid.NewGuid():N}");
        _productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Forbidden Target", CatalogDataHelper.GenerateUniqueSku("FORB"), 42m, 5, _categoryId);
    }

    [TestCaseSource(nameof(AdminOnlyRoutes))]
    public async Task ASignedInNonAdmin_IsForbidden(string route)
    {
        var method = route[..route.IndexOf(' ')];
        var target = route.Contains("/categories") ? _categoryId : _productId;
        var path = route[(method.Length + 1)..]
            .Replace("{id:guid}", target.ToString())
            .Replace("{imageId:guid}", Guid.NewGuid().ToString())
            .Replace("{attributeId:guid}", Guid.NewGuid().ToString());

        var body = BodyFor(route);
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = body is null ? null : JsonContent.Create(body, body.GetType())
        };

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{route} must require the Admin role, not merely a signed-in caller; "
            + $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [Test]
    public void EveryWriteEndpoint_RequiresTheAdminPolicy_AndIsListedHere()
    {
        var writes = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(m => m is not ("GET" or "HEAD"))
                .Select(m => (Route: $"{m} {e.RoutePattern.RawText!.TrimEnd('/')}", Endpoint: e)))
            .ToList();

        writes.Select(w => w.Route).Should().BeEquivalentTo(AdminOnlyRoutes,
            "a write endpoint added or removed must be reflected in AdminOnlyRoutes, so it is sent above");

        foreach (var (route, endpoint) in writes)
        {
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Should().Contain(a => a.Policy == "Admin", $"{route} must carry the Admin policy");
            endpoint.Metadata.GetMetadata<IAllowAnonymous>()
                .Should().BeNull($"{route} must not be anonymous");
        }
    }

    [Test]
    public void EveryReadEndpoint_IsListedHere_WithItsPolicy()
    {
        var reads = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .ToDictionary(e => $"GET {e.RoutePattern.RawText!.TrimEnd('/')}", e => e);

        reads.Keys.Should().BeEquivalentTo(ReadPolicies.Keys,
            "a read endpoint added or removed must be listed in ReadPolicies with the policy it must carry");

        foreach (var (route, expected) in ReadPolicies)
        {
            var metadata = reads[route].Metadata;
            metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).SingleOrDefault(p => p is not null)
                .Should().Be(expected, $"{route} carries the wrong policy");

            // A policy with AllowAnonymous beside it guards nothing, and on a group-mapped endpoint the policy comes
            // from the group, so an .AllowAnonymous() on the one endpoint would leave the policy check above green.
            if (expected is not null)
                metadata.GetMetadata<IAllowAnonymous>().Should().BeNull($"{route} must not be anonymous");
        }
    }

    [TestCase("/api/v1/products/export")]
    [TestCase("/api/v1/products/deleted")]
    public async Task ASignedInNonAdmin_CannotReadAnAdminOnlyProductList(string path)
    {
        using var response = await Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{path} must require the Admin role");
    }

    /// <summary>
    /// The status code alone would pass if the write ran and the response merely reported an error,
    /// so assert the stored state too.
    /// </summary>
    [Test]
    public async Task AForbiddenWrite_ChangesNothing()
    {
        using var scope = Factory.Services.CreateScope();
        var id = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Untouchable", CatalogDataHelper.GenerateUniqueSku("UNTCH"), 42m, 5, _categoryId);

        (await Client.PutAsJsonAsync($"/api/v1/products/{id}",
                new UpdateProductRequest { ProductId = id, Price = 1m, StockQuantity = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.DeleteAsync($"/api/v1/products/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stored = await db.Products.AsNoTracking().IgnoreQueryFilters().SingleAsync(p => p.Id == id);
        stored.Price.Should().Be(42m);
        stored.StockQuantity.Should().Be(5);
        stored.IsDeleted.Should().BeFalse();
    }

    /// <summary>
    /// D1. Draft visibility is decided by role, not by being signed in. The anonymous half is pinned by
    /// <c>ProductVisibilityTests</c>; this is the half that distinguishes "admin" from "any token" —
    /// the same distinction the write policy makes.
    /// </summary>
    [Test]
    public async Task ASignedInNonAdmin_CannotSeeDrafts()
    {
        using var scope = Factory.Services.CreateScope();
        var sku = CatalogDataHelper.GenerateUniqueSku("DRAFT");
        var id = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Draft For Customers", sku, 42m, 5, _categoryId, publish: false);

        (await Client.GetAsync($"/api/v1/products/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/v1/products?PageNumber=1&PageSize=100&IncludeUnpublished=true&SearchTerm={sku}");
        list!.Items.Should().NotContain(p => p.Id == id);
    }

    private object? BodyFor(string route) => route switch
    {
        "POST /api/v1/products" => new CreateProductRequest
        {
            Name = "Forbidden Create",
            Sku = CatalogDataHelper.GenerateUniqueSku("FORBC"),
            Price = 10m,
            StockQuantity = 1,
            CategoryId = _categoryId
        },
        "PUT /api/v1/products/{id:guid}" => new UpdateProductRequest { ProductId = _productId, Price = 99m, StockQuantity = 1 },
        "PUT /api/v1/products/{id:guid}/discount" => new SetProductDiscountRequest { DiscountPrice = 1m },
        "POST /api/v1/products/{id:guid}/images" => new AddProductImageRequest { Url = "https://cdn.example.com/forbidden.jpg" },
        "POST /api/v1/products/{id:guid}/attributes" => new AddProductAttributeRequest { Name = "Color", Value = "Red" },
        "PUT /api/v1/products/{id:guid}/images/{imageId:guid}" => new UpdateProductImageRequest { Url = "https://cdn.example.com/forbidden-edit.jpg" },
        "PUT /api/v1/products/{id:guid}/images/reorder" => new ReorderProductImagesRequest { ImageIds = [Guid.NewGuid()] },
        "PUT /api/v1/products/{id:guid}/attributes" => new ReplaceProductAttributesRequest
        {
            Attributes = [new ReplaceProductAttributeItem { Name = "Color", Value = "Forbidden" }]
        },
        "PUT /api/v1/products/{id:guid}/attributes/{attributeId:guid}" => new UpdateProductAttributeRequest { Name = "Color", Value = "Forbidden" },
        "PATCH /api/v1/products/{id:guid}/stock" => new AdjustProductStockRequest { Delta = 100 },
        "PUT /api/v1/categories/reorder" => new ReorderCategoriesRequest { CategoryIds = [_categoryId] },
        "POST /api/v1/products/bulk/publish" or "POST /api/v1/products/bulk/unpublish" or "POST /api/v1/products/bulk/delete"
            => new BulkProductIdsRequest { ProductIds = [_productId] },
        "POST /api/v1/products/bulk/category" => new BulkChangeCategoryRequest { ProductIds = [_productId], CategoryId = _categoryId },
        "POST /api/v1/products/bulk/price" => new BulkPriceRequest { Items = [new BulkPriceItem { ProductId = _productId, Price = 1m }] },
        "POST /api/v1/products/import" => new ImportProductsRequest
        {
            Products = [new ImportProductRowRequest { Name = "Forbidden Import", Sku = CatalogDataHelper.GenerateUniqueSku("FORBI"), Price = 1m, CategoryId = _categoryId }]
        },
        "PUT /api/v1/categories/{id:guid}/parent" => new MoveCategoryRequest { NewParentCategoryId = null },
        "POST /api/v1/categories" => new CreateCategoryRequest { Name = "Forbidden Category" },
        "PUT /api/v1/categories/{id:guid}" => new UpdateCategoryRequest { Id = _categoryId, Name = "Renamed" },
        _ => null
    };
}
