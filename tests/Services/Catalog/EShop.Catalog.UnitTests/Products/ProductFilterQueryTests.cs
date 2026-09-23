using System.Reflection;
using EShop.Catalog.Application.Products.Queries.ExportProducts;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.UnitTests.Products;

/// <summary>
/// Admin panel S16. <see cref="ProductFilterQuery"/> is what makes the export filter exactly like the list; these pin
/// the one mapping both use.
/// </summary>
[TestFixture]
public class ProductFilterQueryTests
{
    /// <summary>
    /// A filter property that <see cref="ProductFilterQuery.ToFilter"/> forgets would bind, validate and then be ignored —
    /// by the list and the export alike. Each property is set on its own, and the resulting filter must differ from the
    /// empty one. The two sort properties are not filters and are passed separately.
    /// </summary>
    [Test]
    public void EveryFilterProperty_ReachesTheFilter()
    {
        var sample = new Dictionary<string, object>
        {
            [nameof(ProductFilterQuery.CategoryId)] = Guid.NewGuid(),
            [nameof(ProductFilterQuery.SearchTerm)] = "lamp",
            [nameof(ProductFilterQuery.MinPrice)] = 1m,
            [nameof(ProductFilterQuery.MaxPrice)] = 2m,
            [nameof(ProductFilterQuery.Status)] = ProductStatus.Draft,
            [nameof(ProductFilterQuery.HasDiscount)] = true,
            [nameof(ProductFilterQuery.StockBelow)] = 5,
            [nameof(ProductFilterQuery.CreatedFrom)] = DateTime.UtcNow,
            [nameof(ProductFilterQuery.CreatedTo)] = DateTime.UtcNow
        };

        var filterProperties = typeof(ProductFilterQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.Name is not (nameof(ProductFilterQuery.SortBy) or nameof(ProductFilterQuery.IsDescending)))
            .Select(p => p.Name)
            .ToList();

        Assert.That(filterProperties, Is.EquivalentTo(sample.Keys),
            "a property was added to ProductFilterQuery — add a sample for it here, and map it in ToFilter");

        var empty = new ExportProductsQuery().ToFilter(includeUnpublished: true);
        foreach (var (name, value) in sample)
        {
            var query = new ExportProductsQuery();
            typeof(ProductFilterQuery).GetProperty(name)!.SetValue(query, value);

            Assert.That(query.ToFilter(includeUnpublished: true), Is.Not.EqualTo(empty), $"{name} does not reach the filter");
        }
    }

    [Test]
    public void VisibilityComesFromTheCaller_NotFromTheQuery()
    {
        var query = new ExportProductsQuery();

        Assert.Multiple(() =>
        {
            Assert.That(query.ToFilter(includeUnpublished: true).IncludeUnpublished, Is.True);
            Assert.That(query.ToFilter(includeUnpublished: false).IncludeUnpublished, Is.False);
        });
    }

    [Test]
    public void ADateWithNoZone_IsReadAsUtc_ForBothReads()
    {
        // What ?CreatedFrom=2026-09-01 binds to. Npgsql refuses an Unspecified DateTime against timestamptz.
        var unspecified = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var fromList = new GetProductsQuery { CreatedFrom = unspecified, CreatedTo = unspecified }.ToFilter(false);
        var fromExport = new ExportProductsQuery { CreatedFrom = unspecified }.ToFilter(true);

        Assert.Multiple(() =>
        {
            Assert.That(fromList.CreatedFrom!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(fromList.CreatedTo!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(fromList.CreatedFrom, Is.EqualTo(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)), "same wall time, now UTC");
            Assert.That(fromExport.CreatedFrom!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void TheExport_ValidatesTheListsFilterRules()
    {
        var result = new ExportProductsQueryValidator().Validate(new ExportProductsQuery { MinPrice = -1, SearchTerm = "x" });

        Assert.That(result.Errors.Select(e => e.PropertyName),
            Is.SupersetOf(new[] { nameof(ProductFilterQuery.MinPrice), nameof(ProductFilterQuery.SearchTerm) }));
    }
}
