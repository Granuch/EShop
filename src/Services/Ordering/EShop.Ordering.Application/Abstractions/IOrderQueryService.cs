using EShop.Ordering.Application.Orders.Queries;

namespace EShop.Ordering.Application.Abstractions;

/// <summary>
/// Query service for order read operations with filtering and pagination.
/// Implemented in Infrastructure to keep EF Core-specific query logic
/// out of the Application layer.
/// </summary>
public interface IOrderQueryService
{
    /// <summary>
    /// One OFFSET page of the admin order list, plus the total under the same filter.
    /// </summary>
    /// <remarks>
    /// Admin panel S8 replaced the loose <c>status</c> parameter with <see cref="OrderListFilter"/>.
    /// Adding each new filter as its own parameter would have put it before the trailing
    /// <see cref="CancellationToken"/> and broken every call site and Moq setup with a CS1503 that
    /// names the cancellation token rather than the change.
    /// </remarks>
    Task<(List<OrderDto> Items, int TotalCount)> GetOrdersAsync(
        OrderListFilter filter,
        OrderSortBy sortBy,
        bool isDescending,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>One order, through the same projection as the lists (audit L5), or <c>null</c>.</summary>
    Task<OrderDto?> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<(List<OrderDto> Items, int TotalCount)> GetOrdersByUserAsync(
        string userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The numbers behind the admin dashboard (Admin panel S8): totals, a per-status breakdown and
    /// one bucket per period, over orders created within the window.
    /// </summary>
    /// <remarks>
    /// Two round trips, not one per number: the breakdown is a single <c>GROUP BY "Status"</c> and the
    /// buckets a single <c>GROUP BY</c> on date parts. The window-wide totals are summed from the
    /// breakdown in memory rather than queried again, so they cannot disagree with it under a
    /// concurrent write.
    /// </remarks>
    Task<OrderStatsDto> GetOrderStatsAsync(
        DateTime? from,
        DateTime? to,
        OrderStatsGroupBy groupBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One order's operator notes, newest first, or <c>null</c> when the order does not exist.
    /// </summary>
    /// <remarks>
    /// Nullable rather than an empty list, because the two cases have different answers: a missing
    /// order is a 404 and an order nobody has annotated is a 200 with <c>[]</c>. A list cannot carry
    /// that distinction, and returning <c>[]</c> for both would tell a client that every id it invents
    /// names a real order.
    /// </remarks>
    Task<IReadOnlyList<OrderNoteDto>?> GetOrderNotesAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One order's status timeline, oldest first, or <c>null</c> when the order does not exist.
    /// </summary>
    /// <remarks>
    /// An existing order always has at least the row its creation wrote, so an empty list here means
    /// the history was written by something other than the aggregate — which is the bug this design
    /// exists to prevent. The nullability is still what separates 404 from 200; see
    /// <see cref="GetOrderNotesAsync"/>.
    /// </remarks>
    Task<IReadOnlyList<OrderStatusHistoryDto>?> GetOrderStatusHistoryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}
