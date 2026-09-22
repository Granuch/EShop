using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;

namespace EShop.Basket.Application.Queries.Admin;

/// <summary>
/// One stored basket, summarised. The lines themselves are one more call away — <c>GET /api/v1/basket/{userId}</c>,
/// which an admin may read (Basket audit D10).
/// </summary>
public sealed record AdminBasketSummaryDto
{
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// False for a document the basket cannot be read from: the customer sees an empty basket, and the next write moves
    /// the document to <c>basket:corrupt:{userId}</c> (Basket audit L6). Every figure below is then null.
    /// </summary>
    public bool IsReadable { get; init; }

    public int? Lines { get; init; }
    public long? TotalItems { get; init; }
    public decimal? TotalPrice { get; init; }
    public string? Currency { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? LastModifiedAt { get; init; }

    public static AdminBasketSummaryDto From(StoredBasketEntry entry)
        => entry.Basket is { } basket
            ? new AdminBasketSummaryDto
            {
                UserId = entry.UserId,
                IsReadable = true,
                Lines = basket.Items.Count,
                TotalItems = basket.TotalItems,
                TotalPrice = basket.TotalPrice,
                Currency = ShoppingBasket.Currency,
                CreatedAt = basket.CreatedAt,
                LastModifiedAt = basket.LastModifiedAt
            }
            : new AdminBasketSummaryDto { UserId = entry.UserId, IsReadable = false };
}

/// <summary>
/// A page of a basket walk. Pass <see cref="NextCursor"/> back as <c>?cursor=</c> for the next one; it is null once the
/// walk is complete. A page can be shorter than asked for — even empty — while <see cref="NextCursor"/> is still set,
/// because each request scans a bounded number of keys: keep going until it is null.
/// </summary>
public sealed record AdminBasketPageDto
{
    public IReadOnlyList<AdminBasketSummaryDto> Items { get; init; } = [];
    public string? NextCursor { get; init; }

    /// <summary>For <c>/abandoned</c>: the cutoff applied — baskets last changed strictly before it. Null otherwise.</summary>
    public DateTime? ModifiedBefore { get; init; }
}

public sealed record OutboxDeadLetterDto
{
    /// <summary>False for an entry that is not a readable outbox envelope; every other field is then null.</summary>
    public bool IsReadable { get; init; }

    /// <summary>The integration event's id, which is the <c>checkoutId</c> the customer was given (Basket audit D3).</summary>
    public Guid? MessageId { get; init; }

    public string? EventType { get; init; }

    /// <summary>When the checkout happened.</summary>
    public DateTime? OccurredOnUtc { get; init; }

    /// <summary>Null for a message dead-lettered before this was recorded (Admin panel S14).</summary>
    public DateTime? DeadLetteredAtUtc { get; init; }

    /// <summary>Failed publish attempts; null for one never attempted (<c>Unpublishable</c>) or not recorded.</summary>
    public int? Attempts { get; init; }

    /// <summary><c>PublishFailed</c>, <c>PublishTimedOut</c> or <c>Unpublishable</c>; null when not recorded.</summary>
    public string? Error { get; init; }

    /// <summary>The last failure's exception type name — never its message, which can name hosts and users.</summary>
    public string? ExceptionType { get; init; }

    public string? CorrelationId { get; init; }

    public static OutboxDeadLetterDto From(OutboxDeadLetter letter) => new()
    {
        IsReadable = letter.IsReadable,
        MessageId = letter.MessageId,
        EventType = letter.EventType,
        OccurredOnUtc = letter.OccurredOnUtc,
        DeadLetteredAtUtc = letter.DeadLetteredAtUtc,
        Attempts = letter.Attempts,
        Error = letter.FailureReason,
        ExceptionType = letter.ExceptionType,
        CorrelationId = letter.CorrelationId
    };
}

/// <summary>Dead letters, newest first. <see cref="Total"/> is the whole list's length, not this page's.</summary>
public sealed record OutboxDeadLetterPageDto
{
    public long Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public IReadOnlyList<OutboxDeadLetterDto> Items { get; init; } = [];
}
