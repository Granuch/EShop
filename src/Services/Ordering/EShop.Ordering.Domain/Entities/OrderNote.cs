using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Ordering.Domain.Entities;

/// <summary>
/// An internal note an operator attached to an order (Admin panel S9, endpoints #60/#61) — "customer
/// called about the delivery window", "refund approved by finance".
///
/// <para>
/// Append-only, like <see cref="OrderStatusHistory"/>: no edit, no delete. A note is a record of what
/// somebody said at a point in time, and one that can be rewritten afterwards is worth nothing as
/// evidence. If a note is wrong, the next note says so.
/// </para>
/// <para>
/// <b>Not customer-visible.</b> Both note endpoints are Admin-only, and that is a decision, not an
/// oversight: an operator writing "suspected chargeback fraud" has to be able to assume the subject
/// cannot read it.
/// </para>
/// </summary>
public class OrderNote : Entity<Guid>
{
    public const int MaxBodyLength = 2000;
    public const int MaxAuthorIdLength = 100;
    public const int MaxAuthorNameLength = 200;

    public Guid OrderId { get; private set; }

    /// <summary>The operator's user id, taken from the authenticated principal — never from the request body.</summary>
    public string AuthorId { get; private set; } = string.Empty;

    /// <summary>
    /// The operator's display name as it was when they wrote the note, stored rather than resolved on
    /// read: Ordering cannot query Identity for a name, and a note should keep saying who wrote it
    /// after that account is renamed or deleted.
    /// </summary>
    public string AuthorName { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    private OrderNote() { }

    /// <summary>
    /// <c>internal</c> so a note can only be created through <see cref="Order.AddNote"/>, which is what
    /// ties it to an order that exists.
    /// </summary>
    /// <exception cref="DomainException">The author or the body is blank, or a field is too long.</exception>
    internal OrderNote(Guid orderId, string authorId, string authorName, string body)
    {
        if (string.IsNullOrWhiteSpace(authorId))
            throw new DomainException("Note author is required.");
        if (authorId.Length > MaxAuthorIdLength)
            throw new DomainException($"Note author id must not exceed {MaxAuthorIdLength} characters.");
        if (string.IsNullOrWhiteSpace(authorName))
            throw new DomainException("Note author name is required.");
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("Note body is required.");

        var trimmedBody = body.Trim();
        if (trimmedBody.Length > MaxBodyLength)
            throw new DomainException($"Note must not exceed {MaxBodyLength} characters.");

        Id = Guid.NewGuid();
        OrderId = orderId;
        AuthorId = authorId.Trim();

        // The one field that is truncated rather than refused, on purpose: the name comes from the
        // caller's own claims, not from the request, so refusing it would make notes impossible to
        // write for an operator whose display name happens to be long — a failure they could not act
        // on. Everything the request supplies is refused instead.
        var trimmedName = authorName.Trim();
        AuthorName = trimmedName.Length > MaxAuthorNameLength
            ? trimmedName[..MaxAuthorNameLength]
            : trimmedName;

        Body = trimmedBody;
    }
}
