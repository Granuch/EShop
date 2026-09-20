using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Domain.Interfaces;

/// <summary>
/// The read-model side of Payment (Admin panel S10), kept apart from <see cref="IPaymentRepository"/> on purpose: the
/// repository is the write seam and its reads are tracked and used inside transactions, while everything here is
/// <c>AsNoTracking</c> and answers a screen. Ordering and Identity each have the same split; Payment was the outlier.
/// <para>The interface lives in Domain because Application calls it and Infrastructure owns it — the repo's rule for
/// every cross-layer call, and where Payment audit S12 put the Stripe abstractions for the same reason.</para>
/// </summary>
public interface IPaymentQueryService
{
    /// <summary>
    /// One OFFSET page of the admin payment list, plus the total under the same filter. Newest first, with <c>Id</c>
    /// breaking ties — offset paging over a non-unique sort is nondeterministic on Postgres, so a payment could appear
    /// on two pages or on none.
    /// </summary>
    /// <remarks>
    /// Returns entities, not a projection, so the endpoint answers the same <c>PaymentDto</c> as every other payment
    /// read. A second projection is exactly what the plan says not to build here.
    /// </remarks>
    Task<(List<PaymentTransaction> Items, int TotalCount)> GetPageAsync(
        PaymentListFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>How many payments match, with no rows fetched. The export refuses rather than truncates, so it has to
    /// know the size before it starts.</summary>
    Task<int> CountAsync(PaymentListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every matching payment, newest first, capped at <paramref name="maxRows"/>. The cap is a second line of defence
    /// behind the export's own count check, so no call here can load an unbounded table.
    /// </summary>
    Task<List<PaymentTransaction>> ListAsync(
        PaymentListFilter filter,
        int maxRows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The admin dashboard's numbers over one window.
    /// </summary>
    /// <remarks>
    /// Two round trips, not one per number: the breakdown is a single <c>GROUP BY "Status"</c> and the buckets a single
    /// <c>GROUP BY</c> on date parts. The window-wide totals are summed from the breakdown in memory rather than
    /// queried again, so they cannot disagree with it under a concurrent write.
    /// </remarks>
    Task<PaymentStats> GetStatsAsync(
        PaymentListFilter window,
        PaymentStatsGroupBy groupBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a payment with this id exists (Admin panel S11). The timeline read needs it to tell "no such payment"
    /// from "nothing has happened to it yet" — an empty array for an id that was never a payment is a wrong answer
    /// that looks exactly like a right one.
    /// </summary>
    Task<bool> PaymentExistsAsync(Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One payment's timeline, <b>oldest first</b> — it is read as a narrative, and a narrative starts at the
    /// beginning. <c>Id</c> breaks ties, because several rows of one save share an instant to the tick.
    /// </summary>
    Task<List<PaymentEvent>> GetEventsAsync(Guid paymentId, CancellationToken cancellationToken = default);
}
