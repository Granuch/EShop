using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.ImportProducts;

/// <summary>
/// Creates a batch of products from rows (admin panel S16, endpoint #47), with a report per row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Create-only, never update.</b> A row whose SKU a live product already holds is refused as
/// <c>Product.SkuConflict</c>, never merged into it. An import that edits what it matches would be a second, unaudited
/// way to change prices and descriptions in bulk, one whose effect depends on what the file happened to collide with;
/// prices have their own bulk endpoint. A consequence worth knowing: re-sending a file after a partial import refuses
/// the rows that went in the first time, which is the safe way round.
/// </para>
/// <para>
/// <b>A row is accepted exactly when <c>POST /api/v1/products</c> would accept it</b> — each row is checked by the real
/// <see cref="CreateProductCommandValidator"/>, and then against the same SKU and category rules
/// <see cref="CreateProductCommandHandler"/> applies. New products are drafts, as a single create makes them; publishing
/// is a separate, deliberate step (the bulk publish endpoint).
/// </para>
/// <para>
/// <b>JSON rows, not a CSV upload.</b> The API stays typed end to end: every field is bound, validated and reported by
/// row index, and an unknown field is refused as Catalog refuses one everywhere. A client that edits the CSV export turns
/// it back into rows; parsing a spreadsheet is a presentation concern. See <see cref="ProductImportReport"/> for what the
/// answer guarantees.
/// </para>
/// </remarks>
public record ImportProductsCommand : IRequest<Result<ProductImportReport>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    /// <summary>The cap on rows per import. Same bound, and the same refuse-rather-than-truncate rule, as the bulk actions.</summary>
    public const int MaxRows = Bulk.BulkProductLimits.MaxItemsPerRequest;

    string IAuditedCommand.AuditEntityType => "Product";

    string? IAuditedCommand.AuditEntityId => null;

    // One row per import row: the created product's id, or none for a refused row, which still says what was refused.
    IReadOnlyList<AuditedItem>? IAuditedCommand.AuditItemsFromResult(object? value)
        => (value as ProductImportReport)?.Rows
            .Select(r => new AuditedItem(r.ProductId?.ToString(), r.ErrorCode, new { r.Index, r.Sku }))
            .ToList();

    public IReadOnlyList<ImportProductRow>? Products { get; init; }

    // A new product has no detail entry to evict.
    public IEnumerable<string> CacheKeysToInvalidate => [];

    // Once for the whole import, like a single create declares it once.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}

/// <summary>One product to create. The same fields as <c>POST /api/v1/products</c>, minus images and attributes.</summary>
public record ImportProductRow
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public Guid CategoryId { get; init; }

    internal CreateProductCommand ToCreateCommand() => new()
    {
        Name = Name,
        Description = Description,
        Sku = Sku,
        Price = Price,
        StockQuantity = StockQuantity,
        CategoryId = CategoryId
    };
}

/// <summary>The answer to an import.</summary>
/// <param name="Rows">One entry per row sent, <b>in the order sent</b>.</param>
/// <remarks>
/// Every row reported created is committed, and no refused row created anything. The import is one transaction: every
/// refusal is decided before the single save, and a database failure — say another request taking one of these SKUs in
/// the instant between the check and the save — fails the whole request and creates nothing, rather than a report that
/// names products which do not exist.
/// </remarks>
public sealed record ProductImportReport(int Requested, int Created, int Failed, IReadOnlyList<ProductImportRowResult> Rows);

/// <summary>One row's outcome.</summary>
/// <param name="Index">The row's zero-based position in the request.</param>
/// <param name="ProductId">The new product's id; <c>null</c> for a refused row.</param>
/// <param name="ErrorCode">
/// <c>Validation.Failed</c>, <c>Product.SkuConflict</c> (a live product holds it, or another row of this import does),
/// <c>Category.NotFound</c>, or <c>DomainError</c>.
/// </param>
public sealed record ProductImportRowResult(
    int Index,
    string? Sku,
    Guid? ProductId,
    bool Succeeded,
    string? ErrorCode = null,
    string? Error = null);
