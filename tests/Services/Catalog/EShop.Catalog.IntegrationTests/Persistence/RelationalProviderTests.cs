using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Persistence;

/// <summary>
/// Stage 0 / TEST-01's own guard: proof that this suite really runs on PostgreSQL, and that the
/// relational-only paths it was moved for are now reachable.
///
/// <para>
/// Without something like this the move is silently reversible. Removing
/// <c>PostgresCatalogApiFactory</c>'s <c>Testing:UseRelationalDatabase</c> setting, or pointing
/// <c>IntegrationTestBase.CreateFactoryAsync</c> back at the InMemory factory, would drop the suite
/// onto a provider that cannot execute half of what Catalog ships — and every other test in the
/// project would keep passing, because they were written to pass there. These two are the ones that
/// go red.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class RelationalProviderTests : IntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    [Test]
    public void IntegrationTests_RunAgainstNpgsql_NotInMemory()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.Database.ProviderName.Should().Be(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "the suite exists to exercise unique/GIN indexes, ILIKE, decimal precision and column "
            + "length caps, none of which the InMemory provider has");
    }

    /// <summary>
    /// The single search assertion Stage 0 owes; full search coverage (escaping, the trigram
    /// indexes, case handling) is Stage 9's job. This one exists because <b>no test in this project
    /// had ever passed a <c>SearchTerm</c>, and none could</b>: <c>ProductQueryService</c> filters
    /// with <c>EF.Functions.ILike</c>, which the InMemory provider cannot translate, so the request
    /// failed rather than returning the wrong rows.
    /// </summary>
    [Test]
    public async Task SearchTerm_IsExecutedByTheDatabase_RatherThanFailingToTranslate()
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        // Distinctive enough that no seeded or neighbouring row can satisfy the assertion by chance.
        var marker = $"Zaphod{Guid.NewGuid():N}";
        await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider,
            $"Searchable {marker} Widget",
            CatalogDataHelper.GenerateUniqueSku("SRCH"),
            12.34m,
            7,
            categoryId);

        // Lower-cased on purpose: ILIKE is case-insensitive, so this also proves the filter ran in
        // Postgres rather than as a client-side string comparison.
        var response = await Client.GetAsync(
            $"{ProductsEndpoint}?PageNumber=1&PageSize=50&SearchTerm={marker.ToLowerInvariant()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        page.Should().NotBeNull();
        page!.Items.Should().ContainSingle(p => p.Name.Contains(marker));
    }
}
