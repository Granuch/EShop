using System.Text;
using EShop.BuildingBlocks.Infrastructure.Export;
using EShop.Catalog.Application.Products.Queries.GetProducts;

namespace EShop.Catalog.API.Infrastructure.Export;

/// <summary>
/// Renders the product export (admin panel S16, endpoint #48). Presentation, so it lives in the API layer, as Payment's
/// writer does; the field rules — quoting and formula neutralisation — are the shared <see cref="SafeCsv"/>.
/// </summary>
/// <remarks>
/// The columns carry every field an import row takes (<c>Sku</c>, <c>Name</c>, <c>Description</c>, <c>Price</c>,
/// <c>StockQuantity</c>, <c>CategoryId</c>) under the same names, so an edited export maps straight back onto import rows.
/// The rest is read-only context. <b>Formula-neutralised fields come back with their leading apostrophe</b>: a name
/// exported as <c>'=Promo</c> and re-imported unedited is stored as <c>'=Promo</c>. That is the price of the defence, and
/// the alternative — stripping an apostrophe on import — would also eat one a product genuinely starts with.
/// </remarks>
public static class ProductCsvWriter
{
    /// <summary>The header row, and by construction the field order of every data row.</summary>
    public static readonly string[] Columns =
    [
        "Id",
        "Sku",
        "Name",
        "Description",
        "CategoryId",
        "Status",
        "Price",
        "DiscountPrice",
        "StockQuantity",
        "MainImageUrl",
        "CreatedAt"
    ];

    public static string Write(IEnumerable<ProductDto> products)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Columns)).Append(SafeCsv.LineEnd);

        foreach (var product in products)
        {
            builder
                .Append(SafeCsv.Field(product.Id.ToString())).Append(',')
                .Append(SafeCsv.Field(product.Sku)).Append(',')
                .Append(SafeCsv.Field(product.Name)).Append(',')
                .Append(SafeCsv.Field(product.Description)).Append(',')
                .Append(SafeCsv.Field(product.CategoryId.ToString())).Append(',')
                // By name, unlike the JSON API, which sends the enum's number: a spreadsheet has no client-side enum to
                // map "1" back to "Active" with.
                .Append(SafeCsv.Field(product.Status.ToString())).Append(',')
                .Append(SafeCsv.Field(SafeCsv.Decimal(product.Price))).Append(',')
                .Append(SafeCsv.Field(SafeCsv.Decimal(product.DiscountPrice))).Append(',')
                .Append(SafeCsv.Field(product.StockQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append(',')
                .Append(SafeCsv.Field(product.MainImageUrl)).Append(',')
                .Append(SafeCsv.Field(SafeCsv.Timestamp(product.CreatedAt)))
                .Append(SafeCsv.LineEnd);
        }

        return builder.ToString();
    }
}
