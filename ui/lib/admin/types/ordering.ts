// Copied from docs/01-overview/frontend/ordering.md (TypeScript). Keep in step with that file.

// ---- Enums (sent as names; filters take the same names in any case) ----

export type OrderStatus = 'Pending' | 'Paid' | 'Shipped' | 'Delivered' | 'Cancelled' | 'Refunded';

export type OrderStatsGroupBy = 'Day' | 'Month' | 'Year';

export type OrderSortBy = 'CreatedAt' | 'TotalPrice' | 'Status';

export interface ShippingAddress {
  /** 3-150 chars: letters, digits, spaces, . , - / # */
  street: string;
  /** 2-100 chars: letters, spaces, . ' - */
  city: string;
  /** Same rule as city. */
  state: string;
  /** 3-12 chars; 12345 or 12345-6789 when country is US. */
  zipCode: string;
  /** ISO 3166-1 alpha-2, any case; returned upper-case. */
  country: string;
}

export interface OrderItem {
  id: string;
  productId: string;
  productName: string;
  /** Catalog's effective price when the line was added; never re-priced. */
  unitPrice: number;
  quantity: number;
  subTotal: number;
}

export interface Order {
  id: string;
  userId: string;
  totalPrice: number;
  status: OrderStatus;
  /** Stripe intent id or "offline:<reference>"; null until Paid. */
  paymentIntentId: string | null;
  createdAt: string;
  paidAt: string | null;
  shippedAt: string | null;
  deliveredAt: string | null;
  cancelledAt: string | null;
  cancellationReason: string | null;
  shippingAddress: ShippingAddress;
  /** No defined order. */
  items: OrderItem[];
}

// ---- Admin: requests ----

export interface AdminOrderListQuery {
  pageNumber?: number;
  /** Default 10, 1-100. */
  pageSize?: number;
  status?: OrderStatus;
  /** Repeat the parameter: ?statuses=Paid&statuses=Shipped. */
  statuses?: OrderStatus[];
  /** <= 200 chars: substring of userId or paymentIntentId, or an exact order id. */
  search?: string;
  /** ISO instant; inclusive. */
  from?: string;
  /** ISO instant; inclusive. */
  to?: string;
  minTotal?: number;
  maxTotal?: number;
  /** Default CreatedAt. Status sorts alphabetically by name. */
  sortBy?: OrderSortBy;
  /** Default true. */
  isDescending?: boolean;
}

export interface OrderStatsQuery {
  from?: string;
  to?: string;
  /** Default Day. */
  groupBy?: OrderStatsGroupBy;
}

export interface AddOrderNoteRequest {
  /** Not blank, at most 2000 characters before trimming. */
  body: string;
}

// ---- Admin: responses ----

export interface OrderStatusBreakdown {
  status: OrderStatus;
  count: number;
  value: number;
}

export interface OrderStatsBucket {
  /** Midnight UTC of the day, the 1st of the month, or 1 January. */
  periodStart: string;
  orderCount: number;
  grossValue: number;
  paidRevenue: number;
}

export interface OrderStats {
  from: string | null;
  to: string | null;
  groupBy: OrderStatsGroupBy;
  totalOrders: number;
  grossValue: number;
  /** Paid + Shipped + Delivered. */
  paidRevenue: number;
  refundedValue: number;
  cancelledValue: number;
  /** Always six entries, one per status. */
  byStatus: OrderStatusBreakdown[];
  /** Non-empty periods only, oldest first. */
  buckets: OrderStatsBucket[];
}

export interface CreatedOrderNoteResponse {
  id: string;
}

export interface OrderNote {
  id: string;
  orderId: string;
  authorId: string;
  /** The author's name claim when writing (currently the email). */
  authorName: string;
  body: string;
  createdAt: string;
}

export interface OrderStatusHistoryEntry {
  id: string;
  orderId: string;
  /** null on the first row only. */
  fromStatus: OrderStatus | null;
  toStatus: OrderStatus;
  /** Only on a cancellation. */
  reason: string | null;
  /** A user id, or "system" for a message-driven transition. */
  actorId: string | null;
  occurredAt: string;
}
