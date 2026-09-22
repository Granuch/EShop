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
    /// <see cref="AuditEntityIdFromResult"/>, or a batch, which acts on several and names them in its payload.
    /// </summary>
    string? AuditEntityId { get; }

    /// <summary>
    /// The entity id carried by a <b>successful</b> result's value, for commands whose request cannot know it yet.
    /// Called only when <see cref="AuditEntityId"/> is <c>null</c>. Typed per command rather than found by reflection,
    /// because the value is sometimes a <c>Guid</c> and sometimes a response record whose id property has its own name.
    /// </summary>
    string? AuditEntityIdFromResult(object? value) => null;
}
