import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type { PagedResult } from "@/lib/admin/types/common";
import type {
  AddOrderNoteRequest,
  AdminOrderListQuery,
  CreatedOrderNoteResponse,
  Order,
  OrderNote,
  OrderStatusHistoryEntry,
} from "@/lib/admin/types/ordering";

// Every admin endpoint here needs the Admin ROLE at Ordering (RequireRole, not a permission): a caller with
// orders.read but without the role gets an empty 403 from the service (ordering.md "Admin panel").

const order = (id: string) => `/api/v1/orders/${encodeURIComponent(id)}`;

/** GET /api/v1/orders. Not cached by the API. `statuses` repeats its key. */
export async function listOrders(query: AdminOrderListQuery): Promise<PagedResult<Order>> {
  await requirePermission("orders.read");
  return adminFetch<PagedResult<Order>>(buildAdminHref("/api/v1/orders", { ...query }));
}

/** GET /api/v1/orders/{id}. 404 Order.NotFound for an admin; `items` come in no defined order. */
export async function getOrder(id: string): Promise<Order> {
  await requirePermission("orders.read");
  return adminFetch<Order>(order(id));
}

/** Oldest first, unpaged. */
export async function getOrderHistory(id: string): Promise<OrderStatusHistoryEntry[]> {
  await requirePermission("orders.read");
  return adminFetch<OrderStatusHistoryEntry[]>(`${order(id)}/history`);
}

/** Newest first, unpaged (at most 500). Internal: never shown to the customer. */
export async function getOrderNotes(id: string): Promise<OrderNote[]> {
  await requirePermission("orders.read");
  return adminFetch<OrderNote[]>(`${order(id)}/notes`);
}

export async function addOrderNote(id: string, body: AddOrderNoteRequest): Promise<CreatedOrderNoteResponse> {
  await requirePermission("orders.write");
  return adminFetch<CreatedOrderNoteResponse>(`${order(id)}/notes`, {
    method: "POST",
    body: JSON.stringify(body),
    headers: { "Content-Type": "application/json" },
  });
}

/**
 * POST /ship (Paid → Shipped; emails the customer) or /deliver (Shipped → Delivered). A 409 names the wrong state
 * when the order is already further along (F-51), so callers re-read the order to report its real status.
 */
export async function transitionOrder(id: string, transition: "ship" | "deliver"): Promise<void> {
  await requirePermission("orders.write");
  await adminFetch<void>(`${order(id)}/${transition}`, { method: "POST" });
}
