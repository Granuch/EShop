import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type { PagedResult } from "@/lib/admin/types/common";
import type {
  MarkUndeliverableRequest,
  NotificationDetail,
  NotificationListQuery,
  NotificationStats,
  NotificationStatsQuery,
  NotificationSummary,
  NotificationTemplate,
  RetryFailedNotificationsRequest,
  RetryFailedNotificationsResult,
  TestNotificationRequest,
  TestNotificationResult,
} from "@/lib/admin/types/notification";

// Notification checks permissions: notifications.read for the reads, notifications.manage for the four actions.

const notification = (id: string, rest = "") => `/api/v1/notifications/${encodeURIComponent(id)}${rest}`;

function jsonBody(body: unknown): RequestInit {
  return { body: JSON.stringify(body), headers: { "Content-Type": "application/json" } };
}

/** GET /api/v1/notifications: the journal, newest first; `status` repeats its key. */
export async function listNotifications(query: NotificationListQuery): Promise<PagedResult<NotificationSummary>> {
  await requirePermission("notifications.read");
  return adminFetch<PagedResult<NotificationSummary>>(buildAdminHref("/api/v1/notifications", { ...query }));
}

/** GET /stats over the journal's filters; `byStatus` always has five entries. */
export async function getNotificationStats(query: NotificationStatsQuery): Promise<NotificationStats> {
  await requirePermission("notifications.read");
  return adminFetch<NotificationStats>(buildAdminHref("/api/v1/notifications/stats", { ...query }));
}

/** GET /{id}: the full row, with lastError, correlationId, providerMessageId and the resend state. */
export async function getNotification(id: string): Promise<NotificationDetail> {
  await requirePermission("notifications.read");
  return adminFetch<NotificationDetail>(notification(id));
}

/** GET /templates: always the eight templates. */
export async function listNotificationTemplates(): Promise<NotificationTemplate[]> {
  await requirePermission("notifications.read");
  return adminFetch<NotificationTemplate[]>("/api/v1/notifications/templates");
}

/** POST /templates/{name}/test: synchronous; writes no journal row. 503 when the mail server refused it. */
export async function sendTestNotification(name: string, body: TestNotificationRequest): Promise<TestNotificationResult> {
  await requirePermission("notifications.manage");
  return adminFetch<TestNotificationResult>(`/api/v1/notifications/templates/${encodeURIComponent(name)}/test`, {
    method: "POST",
    ...jsonBody(body),
  });
}

/** POST /retry-failed: only Failed rows, oldest first, up to `limit`; 202 means dispatched, not delivered. */
export async function retryFailedNotifications(body: RetryFailedNotificationsRequest): Promise<RetryFailedNotificationsResult> {
  await requirePermission("notifications.manage");
  return adminFetch<RetryFailedNotificationsResult>("/api/v1/notifications/retry-failed", { method: "POST", ...jsonBody(body) });
}

/** POST /{id}/resend: 202 with the row as it was BEFORE the resend; poll it for the outcome. */
export async function resendNotification(id: string): Promise<NotificationDetail> {
  await requirePermission("notifications.manage");
  return adminFetch<NotificationDetail>(notification(id, "/resend"), { method: "POST" });
}

/** POST /{id}/mark-undeliverable: closes the row for good; the reason is stored as lastError. */
export async function markNotificationUndeliverable(id: string, body: MarkUndeliverableRequest): Promise<NotificationDetail> {
  await requirePermission("notifications.manage");
  return adminFetch<NotificationDetail>(notification(id, "/mark-undeliverable"), { method: "POST", ...jsonBody(body) });
}
