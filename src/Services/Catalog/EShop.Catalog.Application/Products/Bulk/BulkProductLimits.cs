using FluentValidation;

namespace EShop.Catalog.Application.Products.Bulk;

/// <summary>
/// The hard caps on a bulk request (risk A8, decision Q5a).
/// </summary>
/// <remarks>
/// <para>
/// <b>Refused above the cap, never truncated.</b> Silently acting on the first thousand of 1 500 ids leaves 500 products
/// in the old state behind a 200, which is the wrong answer that looks like a right one. The request is refused whole,
/// and the message says how many it named.
/// </para>
/// <para>
/// 1 000 mirrors Basket's <c>MaxReplayPerRequest</c>, the precedent risk A8 names. It also bounds the transaction every
/// bulk action runs in: all of a batch is loaded, changed and committed together.
/// </para>
/// </remarks>
public static class BulkProductLimits
{
    public const int MaxItemsPerRequest = 1_000;

    /// <summary>
    /// The rules every id list of a bulk request follows: present, non-empty, within the cap, no empty id, no id twice.
    /// </summary>
    /// <remarks>
    /// A repeated id is refused rather than de-duplicated: it always means the client built its list wrongly, and for a
    /// per-item value (a price) the two copies may disagree, with no right answer for which one wins.
    /// </remarks>
    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>?> BulkProductIds<T>(this IRuleBuilder<T, IReadOnlyList<Guid>?> rule)
        => rule
            .NotEmpty().WithMessage("At least one product id is required")
            .Must(ids => ids is null || ids.Count <= MaxItemsPerRequest)
                .WithMessage(x => $"A bulk request may name at most {MaxItemsPerRequest} products")
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty))
                .WithMessage("Product ids must not be empty")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
                .WithMessage("Each product may appear only once in a bulk request");
}
