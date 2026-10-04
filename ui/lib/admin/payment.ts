import "server-only";

import { apiFetch } from "@/lib/api";
import { adminFetch, toApiError } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type { PagedResult } from "@/lib/admin/types/common";
import type {
  AdminPaymentListQuery,
  FailedStripeWebhookReplayReport,
  Payment,
  PaymentEvent,
  PaymentExportQuery,
  PaymentSimulationDiagnostics,
  PaymentStats,
  PaymentStatsQuery,
  RefundPaymentRequest,
  ReplayFailedStripeWebhooksRequest,
  SettleOfflinePaymentRequest,
  SettlePaymentRequest,
  UserPaymentsQuery,
} from "@/lib/admin/types/payment";

// Payment uses two styles (payment.md "Admin panel", F-09): list, stats, export and events take payments.read;
// offline settle and webhook replay payments.write; the simulator settle, refund and simulation the Admin ROLE (gated
// here by payments.write / payments.read). Error codes are SCREAMING_SNAKE (F-24).

const payment = (id: string, rest = "") => `/api/v1/payments/${encodeURIComponent(id)}${rest}`;

function jsonBody(body: unknown): RequestInit {
  return { body: JSON.stringify(body), headers: { "Content-Type": "application/json" } };
}

/** GET /api/v1/payments: newest first; `status` repeats its key; includes method-None placeholders. */
export async function listPayments(query: AdminPaymentListQuery): Promise<PagedResult<Payment>> {
  await requirePermission("payments.read");
  return adminFetch<PagedResult<Payment>>(buildAdminHref("/api/v1/payments", { ...query }));
}

/** GET /stats: `byStatus` always has six entries; capturedRevenue counts Success only. */
export async function getPaymentStats(query: PaymentStatsQuery): Promise<PaymentStats> {
  await requirePermission("payments.read");
  return adminFetch<PaymentStats>(buildAdminHref("/api/v1/payments/stats", { ...query }));
}

/** GET /export: the raw CSV response (at most 10 000 rows, else 400 EXPORT_TOO_LARGE). */
export async function exportPayments(query: PaymentExportQuery): Promise<Response> {
  await requirePermission("payments.read");
  const response = await apiFetch(buildAdminHref("/api/v1/payments/export", { ...query }), { cache: "no-store" });
  if (!response.ok) throw await toApiError(response);
  return response;
}

/** GET /api/v1/payments/{id}: an admin may read any payment. */
export async function getPayment(id: string): Promise<Payment> {
  await requirePermission("payments.read");
  return adminFetch<Payment>(payment(id));
}

/** GET /{id}/events: oldest first, unpaged. */
export async function getPaymentEvents(id: string): Promise<PaymentEvent[]> {
  await requirePermission("payments.read");
  return adminFetch<PaymentEvent[]>(payment(id, "/events"));
}

/** GET /api/v1/users/{userId}/payments: newest first, without method-None placeholders. */
export async function listUserPayments(userId: string, query: UserPaymentsQuery): Promise<PagedResult<Payment>> {
  await requirePermission("payments.read");
  return adminFetch<PagedResult<Payment>>(buildAdminHref(`/api/v1/users/${encodeURIComponent(userId)}/payments`, { ...query }));
}

/** POST /offline: a Pending payment settled for its recorded amount; the reference must be unique. */
export async function settleOffline(body: SettleOfflinePaymentRequest): Promise<Payment> {
  await requirePermission("payments.write");
  return adminFetch<Payment>("/api/v1/payments/offline", { method: "POST", ...jsonBody(body) });
}

/** POST /api/v1/payments: the simulator settles a Pending payment; read `status` (a Failed settle cancels the order). */
export async function settleWithSimulator(body: SettlePaymentRequest): Promise<Payment> {
  await requirePermission("payments.write");
  return adminFetch<Payment>("/api/v1/payments", { method: "POST", ...jsonBody(body) });
}

/** POST /{id}/refund: in full only; `reason` becomes the payment's errorMessage. */
export async function refundPayment(id: string, body: RefundPaymentRequest): Promise<Payment> {
  // payments.refund: the permission named for this, though Payment itself checks the Admin role (payment.md).
  await requirePermission("payments.refund");
  return adminFetch<Payment>(payment(id, "/refund"), { method: "POST", ...jsonBody(body) });
}

/** POST /webhooks/failed/replay: no ids = every outstanding capture, up to 100. */
export async function replayFailedWebhooks(body: ReplayFailedStripeWebhooksRequest): Promise<FailedStripeWebhookReplayReport> {
  await requirePermission("payments.write");
  return adminFetch<FailedStripeWebhookReplayReport>("/api/v1/payments/webhooks/failed/replay", { method: "POST", ...jsonBody(body) });
}

/** GET /simulation: the simulator's configuration (Admin role). */
export async function getSimulation(): Promise<PaymentSimulationDiagnostics> {
  await requirePermission("payments.read");
  return adminFetch<PaymentSimulationDiagnostics>("/api/v1/payments/simulation");
}
