import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type { PagedResult } from "@/lib/admin/types/common";
import type {
  AddOrderItemRequest,
  AddOrderNoteRequest,
  AdminOrderListQuery,
  CancelOrderRequest,
  CreatedOrderNoteResponse,
  Order,
  OrderNote,
  OrderStats,
  OrderStatsQuery,
  OrderStatusHistoryEntry,
  ShippingAddress,
  UpdateOrderItemQuantityRequest,
  UserOrdersQuery,
} from "@/lib/admin/types/ordering";

// Every admin endpoint here needs the Admin ROLE at Ordering (RequireRole, not a permission): a caller with
// orders.read but without the role gets an empty 403 from the service (ordering.md "Admin panel").

const order = (id: string) => `/api/v1/orders/${encodeURIComponent(id)}`;

/** GET /api/v1/orders. Not cached by the API. `statuses` repeats its key. */
export async function listOrders(query: AdminOrderListQuery): Promise<PagedResult<Order>> {
  await requirePermission("orders.read");
  return adminFetch<PagedResult<Order>>(buildAdminHref("/api/v1/orders", { ...query }));
}

/** GET /api/v1/orders/stats. Not cached; `byStatus` always has six entries, `buckets` only non-empty periods. */
export async function getOrderStats(query: OrderStatsQuery): Promise<OrderStats> {
  await requirePermission("orders.read");
  return adminFetch<OrderStats>(buildAdminHref("/api/v1/orders/stats", { ...query }));
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

// ---- Owner-or-admin writes: Pending only for items and cancel; Pending or Paid for the address ----

function jsonBody(body: unknown): RequestInit {
  return { body: JSON.stringify(body), headers: { "Content-Type": "application/json" } };
}

/** POST /cancel: Pending only (409 Order.NotCancellable otherwise); the pending payment is cancelled shortly after. */
export async function cancelOrder(id: string, body: CancelOrderRequest): Promise<void> {
  await requirePermission("orders.write");
  await adminFetch<void>(`${order(id)}/cancel`, { method: "POST", ...jsonBody(body) });
}

/** POST /items: priced from Catalog now; 503 Catalog.Unavailable if Catalog cannot be reached. */
export async function addOrderItem(id: string, body: AddOrderItemRequest): Promise<void> {
  await requirePermission("orders.write");
  await adminFetch<void>(`${order(id)}/items`, { method: "POST", ...jsonBody(body) });
}

/** PUT /items/{itemId}: the line's id, not the product's; the unit price is kept. */
export async function updateOrderItemQuantity(id: string, itemId: string, body: UpdateOrderItemQuantityRequest): Promise<void> {
  await requirePermission("orders.write");
  await adminFetch<void>(`${order(id)}/items/${encodeURIComponent(itemId)}`, { method: "PUT", ...jsonBody(body) });
}

/** DELETE /items/{itemId}: the last line is refused (400 DomainError); cancel the order instead. */
export async function removeOrderItem(id: string, itemId: string): Promise<void> {
  await requirePermission("orders.write");
  await adminFetch<void>(`${order(id)}/items/${encodeURIComponent(itemId)}`, { method: "DELETE" });
}

/** PUT /shipping-address: a full replacement (all five fields); Pending or Paid only (409 otherwise). */
export async function updateShippingAddress(id: string, body: ShippingAddress): Promise<void> {
  await requirePermission("orders.write");
  await adminFetch<void>(`${order(id)}/shipping-address`, { method: "PUT", ...jsonBody(body) });
}

/** GET /api/v1/users/{userId}/orders: newest first. Send userId exactly as stored: the query is case-sensitive (F-48). */
export async function listUserOrders(userId: string, query: UserOrdersQuery): Promise<PagedResult<Order>> {
  await requirePermission("orders.read");
  return adminFetch<PagedResult<Order>>(buildAdminHref(`/api/v1/users/${encodeURIComponent(userId)}/orders`, { ...query }));
}
