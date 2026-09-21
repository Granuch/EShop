using EShop.BuildingBlocks.Application;
using EShop.Notification.Application.Notifications.Queries.GetNotificationById;

namespace EShop.Notification.Application.Notifications.Common;

/// <summary>
/// The error codes the operator's actions answer with (Admin panel S13). Named once, so a handler, the endpoint's status
/// mapping and a test cannot disagree about a string — the endpoint maps each code to its status in one switch.
/// </summary>
public static class NotificationErrors
{
    /// <summary>404. The same string the journal's detail read answers with.</summary>
    public const string NotFoundCode = GetNotificationByIdQueryHandler.NotFoundCode;

    /// <summary>409. Sent or undeliverable: nothing will ever be attempted again.</summary>
    public const string FinalCode = "Notification.Final";

    /// <summary>409. Another attempt holds a live lease.</summary>
    public const string AttemptInProgressCode = "Notification.AttemptInProgress";

    /// <summary>409. The event was not kept (a password reset, or a row older than the payload column).</summary>
    public const string NotResendableCode = "Notification.NotResendable";

    /// <summary>503. This host runs no message bus, so a resend has nowhere to go.</summary>
    public const string BusUnavailableCode = "Notification.BusUnavailable";

    /// <summary>503. The bus refused the resend.</summary>
    public const string DispatchFailedCode = "Notification.DispatchFailed";

    /// <summary>404. No template by that name.</summary>
    public const string TemplateNotFoundCode = "Notification.TemplateNotFound";

    /// <summary>
    /// 503. The SMTP server refused the test send or could not be reached. 503 rather than 502 on purpose: the gateway's
    /// <c>NotificationProxyGuardMiddleware</c> rewrites every 502 into its own "service unavailable" body, which would
    /// replace this one and its error code.
    /// </summary>
    public const string TestSendFailedCode = "Notification.TestSendFailed";

    public static Error NotFound(Guid id) => new(NotFoundCode, $"Notification {id} was not found.");

    public static Error Final(Guid id, string status)
        => new(FinalCode, $"Notification {id} is {status}; nothing is attempted again once a notification is final.");

    public static Error AttemptInProgress(Guid id)
        => new(AttemptInProgressCode,
            $"An attempt to deliver notification {id} is in progress. Wait for it to end — at most "
            + "the attempt lease — and look again.");
}
