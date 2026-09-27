using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;

namespace EShop.Notification.Application.Notifications.Common;

/// <summary>
/// One row of the admin journal (#70). Deliberately narrower than <see cref="NotificationDetailDto"/>: a list page is
/// 50 rows and does not need each one's failure text, correlation id or provider message id.
/// </summary>
public sealed record NotificationSummaryDto(
    Guid Id,
    Guid EventId,
    string EventType,
    string TemplateName,
    string Subject,
    string Status,
    string? RecipientEmail,
    string? UserId,
    int RetryCount,
    bool HasError,
    DateTime CreatedAt,
    DateTime? SentAt,
    DateTime? UpdatedAt);

/// <summary>
/// One notification in full (#71). The three fields the summary omits — <see cref="LastError"/>,
/// <see cref="CorrelationId"/> and <see cref="ProviderMessageId"/> — are the whole reason this endpoint exists:
/// <c>CorrelationId</c> is what ties the row back to the request that caused the event, and <c>LastError</c> is what
/// says why nothing arrived.
/// <para><see cref="IsResendable"/> (Admin panel S13): not final, and its event was kept. A resend can still answer 409
/// while an attempt holds its lease — that is momentary, and deciding it here would need a clock.</para>
/// <para>The stored payload itself is deliberately not exposed: the journal answers "what happened", and the event
/// is Notification's own copy of another service's data.</para>
/// </summary>
public sealed record NotificationDetailDto(
    Guid Id,
    Guid EventId,
    string EventType,
    string TemplateName,
    string Subject,
    string Status,
    string? RecipientEmail,
    string? UserId,
    string? CorrelationId,
    int RetryCount,
    string? LastError,
    string? ProviderMessageId,
    DateTime CreatedAt,
    DateTime? SentAt,
    DateTime? UpdatedAt,
    DateTime? AttemptStartedAt,
    bool IsFinal,
    bool IsResendable);

public sealed record NotificationStatusCountDto(string Status, int Count);

/// <summary>One email template (#75). <see cref="Resendable"/> is false only for the password reset.</summary>
public sealed record NotificationTemplateDto(string Name, string EventType, bool Resendable);

/// <summary>What a test send (#76) sent: the template, and the Message-ID a mail server's logs will show.</summary>
public sealed record TestNotificationResultDto(string TemplateName, string ProviderMessageId);

/// <summary>
/// What a batch retry (#73) did. <see cref="Matching"/> is how many failed, resendable notifications the filter matched
/// when the request ran; when it exceeds the two id lists together, the rest are waiting for a later call.
/// <para>
/// <b>Dispatched is not delivered.</b> Each id was handed to this service's own consumer queue; the delivery itself
/// happens there, and the journal shows its outcome. <see cref="FailedIds"/> are the ones the bus refused outright.
/// </para>
/// </summary>
public sealed record RetryFailedNotificationsResultDto(
    int Matching,
    int Limit,
    IReadOnlyList<Guid> DispatchedIds,
    IReadOnlyList<Guid> FailedIds);

/// <summary>The journal dashboard (#77): sent / failed / queued, plus the full breakdown behind them.</summary>
public sealed record NotificationStatsDto(
    DateTime? From,
    DateTime? To,
    int Total,
    int Sent,
    int Failed,
    int Queued,
    int Undeliverable,
    IReadOnlyList<NotificationStatusCountDto> ByStatus);

public static class NotificationMappings
{
    /// <remarks>
    /// <see cref="NotificationStatus"/> is serialised by <b>name</b>, not by its stored integer. The column is
    /// <c>HasConversion&lt;int&gt;()</c>, so the numbers are a storage detail; a client that read <c>3</c> and called it
    /// "Failed" (it is <c>Sending</c>) would be wrong in the one place an operator is looking for a problem.
    /// </remarks>
    public static NotificationSummaryDto ToSummaryDto(this NotificationLog log) => new(
        log.Id,
        log.EventId,
        log.EventType,
        log.TemplateName,
        log.Subject,
        log.Status.ToString(),
        log.RecipientEmail,
        log.UserId,
        log.RetryCount,
        log.LastError is not null,
        log.CreatedAt,
        log.SentAt,
        log.UpdatedAt);

    public static NotificationDetailDto ToDetailDto(this NotificationLog log) => new(
        log.Id,
        log.EventId,
        log.EventType,
        log.TemplateName,
        log.Subject,
        log.Status.ToString(),
        log.RecipientEmail,
        log.UserId,
        log.CorrelationId,
        log.RetryCount,
        log.LastError,
        log.ProviderMessageId,
        log.CreatedAt,
        log.SentAt,
        log.UpdatedAt,
        log.AttemptStartedAt,
        log.IsFinal,
        !log.IsFinal && log.Payload is not null);

    public static NotificationStatsDto ToDto(this NotificationJournalStats stats) => new(
        stats.From,
        stats.To,
        stats.Total,
        stats.Sent,
        stats.Failed,
        stats.Queued,
        stats.Undeliverable,
        stats.ByStatus.Select(s => new NotificationStatusCountDto(s.Status.ToString(), s.Count)).ToList());
}
