namespace EShop.BuildingBlocks.Application.Auditing;

/// <summary>
/// Marks a command whose every execution is recorded in the service's <c>audit_log</c> (admin panel decision Q8a).
///
/// <para>
/// <b>Which commands carry it:</b> every command an admin-authorized endpoint can send — including the
/// owner-or-admin ones, since "who changed this order?" has the same answer shape whether the customer or an operator
/// did it. Customer-only commands (login, checkout, profile) and commands sent by consumers do not. Each service pins
/// its classification in a test that lists every command in its Application assembly as audited or not, so a new
/// command cannot ship unclassified.
/// </para>
///
/// <para>
/// <b>Implement the members explicitly</b> (<c>string IAuditedCommand.AuditEntityType =&gt; "Product";</c>). An
/// explicit implementation is not a public property, so it stays out of request-body binding, out of the OpenAPI
/// schema, and out of the payload the audit stores — the payload is the request's public properties only.
/// </para>
/// </summary>
public interface IAuditedCommand
{
    /// <summary>The kind of thing acted on, as a stable noun: <c>Product</c>, <c>User</c>, <c>Order</c>.</summary>
    string AuditEntityType { get; }

    /// <summary>
    /// The id of the thing acted on, or <c>null</c> when the request does not know it — a create, whose id comes from
    /// <see cref="AuditEntityIdFromResult"/>, or a batch, whose items come from <see cref="AuditItemsFromResult"/>.
    /// </summary>
    string? AuditEntityId { get; }

    /// <summary>
    /// The entity id carried by a <b>successful</b> result's value, for commands whose request cannot know it yet.
    /// Called only when <see cref="AuditEntityId"/> is <c>null</c>. Typed per command rather than found by reflection,
    /// because the value is sometimes a <c>Guid</c> and sometimes a response record whose id property has its own name.
    /// </summary>
    string? AuditEntityIdFromResult(object? value) => null;

    /// <summary>
    /// For a <b>batch</b> command (admin panel S16): one entry per item it acted on, read from a successful result's
    /// value. When this returns a non-empty list the audit writes <b>one row per item</b> — that item's entity id and its
    /// own outcome — instead of the single row with a null entity id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the payload cannot carry a batch. The payload is the request rendered by
    /// <c>SafeRequestRenderer</c>, which cuts every collection after 25 items, so a thousand-product bulk delete recorded
    /// as one row would name 25 products and leave 975 with no trace — and "who deleted this product?" is answered by the
    /// <c>(EntityType, EntityId)</c> index, which a payload is not in either way.
    /// </para>
    /// <para>
    /// Only a successful result is asked. A batch refused as a whole (validation) or thrown is still one row, like any
    /// other command, because no item was acted on.
    /// </para>
    /// </remarks>
    IReadOnlyList<AuditedItem>? AuditItemsFromResult(object? value) => null;
}

/// <summary>One item of a batch command, as the audit records it. See <see cref="IAuditedCommand.AuditItemsFromResult"/>.</summary>
/// <param name="EntityId">The item's id, or <c>null</c> when it never got one — an import row that was refused.</param>
/// <param name="ErrorCode">
/// <c>null</c> when this item succeeded; otherwise its own error code, recorded as a rejected row. One item failing does
/// not make its neighbours' rows rejected, because it did not stop them.
/// </param>
/// <param name="Detail">
/// What the row stores as its payload: the part of the request that concerns this item (a new price, a target category),
/// rendered through the same redaction as a whole request. <c>null</c> stores no payload.
/// </param>
public sealed record AuditedItem(string? EntityId, string? ErrorCode = null, object? Detail = null);
