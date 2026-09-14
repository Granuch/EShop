using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// M14 (Catalog audit Stage 9). <c>SearchTerm</c> on the product list endpoints.
///
/// <para>
/// Until Stage 0 no test could pass a search term at all — <c>ProductQueryService</c> filters with
/// <c>EF.Functions.ILike</c>, which EF InMemory cannot translate — and until this fixture only one
/// did (<c>RelationalProviderTests</c>' smoke test). So the LIKE escaping, the SKU half of the
/// predicate, the trim and the trigram indexes were all unexercised.
/// </para>
///
/// <para>
/// Every test seeds rows under its own random marker and searches for it, because the fixture's
/// tests share one database. The marker also makes each request's cache key unique, which matters:
/// rows seeded straight through the DbContext bypass cache invalidation.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class ProductSearchTests : IntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    private static string NewMarker() => $"q{Guid.NewGuid():N}";

    private async Task<Guid> SeedAsync(string name, string? sku = null, decimal price = 10m, bool publish = true)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        return await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, name, sku ?? CatalogDataHelper.GenerateUniqueSku("SRCH"), price, 5, categoryId, publish);
    }

    private async Task<PagedResponse<ProductResponse>> SearchAsync(string term, string extra = "")
    {
        using var response = await Client.GetAsync(
            $"{ProductsEndpoint}?PageNumber=1&PageSize=100&SearchTerm={Uri.EscapeDataString(term)}{extra}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>())!;
    }

    [Test]
    public async Task MatchesAnywhereInTheName_IgnoringCase()
    {
        var marker = NewMarker();
        var id = await SeedAsync($"Alpha {marker} Omega");

        // A slice from the middle, upper-cased: neither a prefix match nor a case-sensitive one passes.
        var page = await SearchAsync(marker.Substring(3, 20).ToUpperInvariant());

        page.Items.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Test]
    public async Task MatchesTheSku()
    {
        var marker = NewMarker();
        var id = await SeedAsync("A Name Without The Marker", sku: $"SKU-{marker}");

        var page = await SearchAsync(marker);

        page.Items.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    /// <summary>
    /// Unescaped, <c>%</c> is a wildcard and <c>"{m} 50%"</c> would also match <c>"{m} 50 bucks"</c>.
    /// </summary>
    [Test]
    public async Task APercentSign_IsMatchedLiterally()
    {
        var marker = NewMarker();
        var literal = await SeedAsync($"{marker} 50% off");
        await SeedAsync($"{marker} 50 bucks off");

        var page = await SearchAsync($"{marker} 50%");

        page.Items.Should().ContainSingle().Which.Id.Should().Be(literal);
    }

    /// <summary>Unescaped, <c>_</c> matches any single character, so <c>"{m}_x"</c> would match <c>"{m}Ax"</c>.</summary>
    [Test]
    public async Task AnUnderscore_IsMatchedLiterally()
    {
        var marker = NewMarker();
        var literal = await SeedAsync($"{marker}_x");
        await SeedAsync($"{marker}Ax");

        var page = await SearchAsync($"{marker}_x");

        page.Items.Should().ContainSingle().Which.Id.Should().Be(literal);
    }

    /// <summary>
    /// The escape character itself. If the backslash were not doubled first, <c>\x</c> in the pattern
    /// would mean "a literal x" — so the search would find the row <b>without</b> the backslash and
    /// miss the one with it.
    /// </summary>
    [Test]
    public async Task ABackslash_IsMatchedLiterally()
    {
        var marker = NewMarker();
        var literal = await SeedAsync($"{marker}\\x");
        await SeedAsync($"{marker}x");

        var page = await SearchAsync($"{marker}\\x");

        page.Items.Should().ContainSingle().Which.Id.Should().Be(literal);
    }

    [Test]
    public async Task SurroundingWhitespace_IsIgnored()
    {
        var marker = NewMarker();
        var id = await SeedAsync($"Gamma {marker}");

        var page = await SearchAsync($"  {marker}  ");

        page.Items.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    /// <summary>The count is taken after the search filter, so it describes what the caller can page through.</summary>
    [Test]
    public async Task ComposesWithOtherFilters_AndCountsOnlyTheMatches()
    {
        var marker = NewMarker();
        await SeedAsync($"Cheap {marker}", price: 10m);
        await SeedAsync($"Middle {marker}", price: 20m);
        await SeedAsync($"Dear {marker}", price: 30m);

        var page = await SearchAsync(marker, "&MinPrice=15");

        page.TotalCount.Should().Be(2);
        page.Items.Select(p => p.Price).Should().BeEquivalentTo([20m, 30m]);
    }

    [Test]
    public async Task ADraft_IsNotFoundByAnonymousSearch()
    {
        var marker = NewMarker();
        await SeedAsync($"Draft {marker}", publish: false);

        var page = await SearchAsync(marker);

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Test]
    public async Task TheNewestEndpoint_AppliesTheSameSearch()
    {
        var marker = NewMarker();
        await SeedAsync($"Newest {marker} One");
        await SeedAsync($"Newest {marker} Two");
        await SeedAsync($"Newest Unrelated {NewMarker()}");

        var page = await Client.GetFromJsonAsync<CursorPagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}/newest?PageSize=100&SearchTerm={marker}");

        page!.Items.Should().HaveCount(2).And.OnlyContain(p => p.Name.Contains(marker));
    }

    [TestCase(1)]
    [TestCase(201)]
    public async Task ASearchTermOutsideTheAllowedLength_IsRejected(int length)
    {
        using var response = await Client.GetAsync(
            $"{ProductsEndpoint}?PageNumber=1&PageSize=10&SearchTerm={new string('a', length)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Validation.Failed");
    }

    /// <summary>
    /// The GIN trigram indexes are why search does not scan the table. With sequential scans priced
    /// out, the planner must reach for both of them to serve the predicate shape
    /// <c>ProductQueryService</c> emits — <c>ILIKE … ESCAPE '\'</c> on Name OR Sku. This fails if
    /// either index is dropped, stops being a trigram index, or the predicate moves to a form a
    /// trigram index cannot serve.
    /// </summary>
    [Test]
    public async Task TheTrigramIndexes_CanServeTheSearchPredicate()
    {
        using var scope = Factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.GetConnectionString();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var disableSeqScan = new NpgsqlCommand("SET LOCAL enable_seqscan = off", connection, transaction))
        {
            await disableSeqScan.ExecuteNonQueryAsync();
        }

        await using var explain = new NpgsqlCommand(
            """
            EXPLAIN SELECT "Id" FROM "Products"
            WHERE "Name" ILIKE '%widget%' ESCAPE '\' OR "Sku" ILIKE '%widget%' ESCAPE '\'
            """,
            connection,
            transaction);

        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        string.Join('\n', plan).Should()
            .Contain("IX_Products_Name_Trgm").And
            .Contain("IX_Products_Sku_Trgm");
    }
}
