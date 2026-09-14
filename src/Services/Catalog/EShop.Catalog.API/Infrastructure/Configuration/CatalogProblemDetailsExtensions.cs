using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EShop.Catalog.API.Infrastructure.Configuration;

/// <summary>
/// Catalog-specific branches for <c>AddEShopProblemDetails</c>.
/// </summary>
public static class CatalogProblemDetailsExtensions
{
    /// <summary>
    /// Name of the partial unique index restored by
    /// <c>20260909114056_RestoreProductSkuAndNameIndexes</c>. Kept as a constant because this
    /// mapper and <c>CatalogDbContext.OnModelCreating</c> have to agree on it, and a rename in
    /// either place would otherwise silently downgrade the response to a generic 409.
    /// </summary>
    private const string ProductSkuIndexName = "IX_Products_Sku";

    /// <summary>
    /// Turns a unique-violation on <c>IX_Products_Sku</c> into a 409 whose <c>errorCode</c> is the
    /// domain's own <c>Product.SkuConflict</c>, matching what
    /// <c>CreateProductCommandHandler</c>'s read-then-write check returns.
    ///
    /// <para>
    /// This exists because the check and the constraint answer different questions.
    /// <c>CreateProductCommandHandler</c> reads first and rejects a SKU it can see, which is the
    /// ordinary case and stays a 400. The database only rejects a SKU that appeared <b>between</b>
    /// that read and the insert — a genuine concurrent conflict, hence 409, and retryable. Without
    /// this branch that race surfaced as <c>DuplicateResource</c>, indistinguishable from any other
    /// unique violation in the service.
    /// </para>
    ///
    /// <para>
    /// <b>Register before <c>AddEfDuplicateKey()</c></b> — mappers are first-match-wins, and that
    /// one matches every unique violation.
    /// </para>
    ///
    /// <para>
    /// Matching is on <c>PostgresException.ConstraintName</c> rather than on the message text: the
    /// name is a structured field, whereas the message is localised and carries the offending
    /// value. Nothing from the exception reaches the caller — <c>detail</c> is our own string, per
    /// the rule that framework and database messages stay out of responses.
    /// </para>
    /// </summary>
    public static ProblemDetailsExceptionOptions AddProductSkuConflict(
        this ProblemDetailsExceptionOptions options)
        => options.Add((exception, context) =>
            exception is DbUpdateException { InnerException: PostgresException postgresEx }
            && postgresEx.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresEx.ConstraintName == ProductSkuIndexName
                ? EShopProblem.Create(
                    context,
                    StatusCodes.Status409Conflict,
                    detail: "Another product with the same SKU was created concurrently. Retry with a different SKU.",
                    errorCode: "Product.SkuConflict")
                : null);

    /// <summary>
    /// The two IsActive-filtered unique slug indexes on <c>Categories</c> (roots, and per parent).
    /// Must match the names in <c>CatalogDbContext.OnModelCreating</c>.
    /// </summary>
    private static readonly HashSet<string> CategorySlugIndexNames =
        ["IX_Categories_ParentCategoryId_Slug", "IX_Categories_Slug"];

    /// <summary>
    /// M9 (Catalog audit Stage 8). The category counterpart of <see cref="AddProductSkuConflict"/>:
    /// <c>CreateCategoryCommandHandler</c>'s read-then-write check answers an ordinary duplicate slug
    /// with a 400 <c>Category.SlugConflict</c>; this answers the concurrent one that slipped past it
    /// with a 409 carrying the same code, instead of the generic <c>DuplicateResource</c> every other
    /// unique violation gets. Register before <c>AddEfDuplicateKey()</c>.
    /// </summary>
    public static ProblemDetailsExceptionOptions AddCategorySlugConflict(
        this ProblemDetailsExceptionOptions options)
        => options.Add((exception, context) =>
            exception is DbUpdateException { InnerException: PostgresException postgresEx }
            && postgresEx.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresEx.ConstraintName is { } constraint
            && CategorySlugIndexNames.Contains(constraint)
                ? EShopProblem.Create(
                    context,
                    StatusCodes.Status409Conflict,
                    detail: "Another category with the same slug was created concurrently at this level. Retry with a different slug.",
                    errorCode: "Category.SlugConflict")
                : null);
}
