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
    bool IsFinal);

public sealed record NotificationStatusCountDto(string Status, int Count);

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
        log.IsFinal);

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
