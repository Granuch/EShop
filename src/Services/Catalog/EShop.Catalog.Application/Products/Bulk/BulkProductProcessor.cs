using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;

namespace EShop.Catalog.Application.Products.Bulk;

/// <summary>
/// The one loop behind every bulk product action: load the batch in one query, apply the change product by product,
/// save once.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why applying to a product can be caught per product, and why that is safe only because of the domain.</b> A
/// refusal the product's own rules raise is a <see cref="DomainException"/>, and it is caught here and reported against
/// that product while the rest of the batch goes on. That is correct only because every <c>Product</c> method a bulk
/// action calls checks <i>before</i> it assigns anything — so a refused product is left exactly as loaded, and the single
/// save below writes nothing for it. A mutator that assigned first and threw afterwards would have its half-change
/// committed with its neighbours. <c>BulkProductActionsTests.ARefusedProduct_IsLeftExactlyAsItWas</c> is the guard.
/// </para>
/// <para>
/// <b>A database failure is not caught, and must not be.</b> It fails the whole request and rolls the whole batch back,
/// through <c>TransactionBehavior</c>. Catching one here and reporting it as a row would be the 25P02 trap: Postgres has
/// already aborted the transaction, and the commit the behavior performs afterwards would fail anyway.
/// </para>
/// <para>
/// <b>Not one command per product.</b> Sending the single-product commands through MediatR would bump the
/// <c>products:list</c> family once per product, open a nested transaction per product and write one audit row per
/// product per command. The batch is one command, so the family is bumped once (S16's named risk).
/// </para>
/// </remarks>
public static class BulkProductProcessor
{
    /// <summary>The error code a domain refusal carries, matching what a single-product request answers with.</summary>
    public const string DomainErrorCode = "DomainError";

    public static async Task<BulkProductReport> ApplyAsync(
        IReadOnlyList<Guid> productIds,
        IProductRepository products,
        IUnitOfWork unitOfWork,
        Action<Product> apply,
        CancellationToken cancellationToken)
    {
        var loaded = (await products.GetByIdsWithoutChildrenAsync(productIds, cancellationToken))
            .ToDictionary(p => p.Id);

        var items = new List<BulkProductItemResult>(productIds.Count);
        foreach (var productId in productIds)
        {
            if (!loaded.TryGetValue(productId, out var product))
            {
                // A soft-deleted product lands here too, through the !IsDeleted global filter — the same 404 a single
                // request for it gets.
                items.Add(BulkProductItemResult.Failure(
                    productId, "Product.NotFound", $"Product with ID '{productId}' was not found."));
                continue;
            }

            try
            {
                apply(product);
                items.Add(BulkProductItemResult.Success(productId));
            }
            catch (DomainException ex)
            {
                items.Add(BulkProductItemResult.Failure(productId, DomainErrorCode, ex.Message));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BulkProductReport.From(items);
    }
}
