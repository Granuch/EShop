using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Domain;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Application.Notifications.Queries;
using EShop.Notification.Domain.Interfaces;
using MediatR;

namespace EShop.Notification.Application.Notifications.Commands.RetryFailedNotifications;

/// <summary>
/// Sends every <b>Failed</b> notification the filter matches again, up to <see cref="Limit"/> of them, oldest first
/// (Admin panel S13, endpoint #73). Bound from an optional JSON body: an empty body means "the oldest failed ones,
/// whatever they are".
///
/// <para>
/// <b>Failed only, and there is no status field to say otherwise.</b> Sent and Undeliverable are final, a live Sending
/// row belongs to its attempt, and Pending is a row whose first attempt has not started — none of them is what "retry
/// the failures" means. A single stuck row of those kinds is what the per-notification resend (#72) is for.
/// </para>
///
/// <para>
/// A class-style record rather than a positional one because <c>[SensitiveData]</c> has to land on the
/// <i>property</i> for <c>LoggingBehavior</c> to see it; on a positional parameter it would target the parameter and
/// redact nothing.
/// </para>
/// </summary>
public sealed record RetryFailedNotificationsCommand : IRequest<Result<RetryFailedNotificationsResultDto>>, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Notification";

    string? IAuditedCommand.AuditEntityId => null;

    /// <summary>
    /// The hard cap on one request (risk A8), and the default. Payment's <c>MaxReplayPerRequest</c> is 100 too, and for a
    /// related reason: each id dispatched here becomes, a moment later, an Identity lookup and an SMTP send on the
    /// consumer side. A thousand at once is a burst against the mail server that this service's own retry policy would
    /// then have to absorb.
    /// </summary>
    public const int MaxRetryPerRequest = 100;

    /// <summary>The integration event's type name, e.g. <c>OrderCreatedEvent</c>. Case-insensitive.</summary>
    public string? EventType { get; init; }

    /// <summary>The template, e.g. <c>order-created</c>. Case-insensitive.</summary>
    public string? TemplateName { get; init; }

    /// <summary>Exact match on the recipient's user id.</summary>
    public string? UserId { get; init; }

    /// <summary>The recipient's address. Case-insensitive. Personal data, so redacted from the request log.</summary>
    [SensitiveData]
    public string? Email { get; init; }

    /// <summary>Inclusive lower bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Inclusive upper bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? To { get; init; }

    /// <summary>How many to dispatch, 1 to <see cref="MaxRetryPerRequest"/>. Omitted: the cap.</summary>
    public int? Limit { get; init; }

    public int EffectiveLimit => Limit ?? MaxRetryPerRequest;

    /// <summary>
    /// The journal's own filter, with the dates coerced to UTC exactly as the list's are: a JSON body's
    /// <c>"2026-09-01"</c> binds as <c>Kind.Unspecified</c>, the same trap as the query string's.
    /// </summary>
    public NotificationJournalFilter ToFilter() => new(
        [],
        EventType,
        TemplateName,
        UserId,
        Email,
        NotificationQueryEnums.AsUtc(From),
        NotificationQueryEnums.AsUtc(To),
        null);
}
