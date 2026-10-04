import { endOfDayUtc, startOfDayUtc } from "@/lib/admin/format";
import type { QueryValue } from "@/lib/admin/href";
import type { AdminPaymentListQuery, PaymentFilterQuery, PaymentMethod, PaymentStatus } from "@/lib/admin/types/payment";

export const PAYMENTS_PATH = "/adminPanel/payments";
export const DEFAULT_PAGE_SIZE = 20;

export const PAYMENT_STATUSES: PaymentStatus[] = ["Pending", "Processing", "Success", "Failed", "Refunded", "Cancelled"];
export const PAYMENT_METHODS: PaymentMethod[] = ["Stripe", "Mock", "None"];

export interface PaymentFilters {
  status: PaymentStatus[];
  paymentMethod: PaymentMethod | "";
  orderId: string;
  userId: string;
  /** YYYY-MM-DD, as the date inputs hold them. */
  from: string;
  to: string;
  minAmount: string;
  maxAmount: string;
  pageNumber: number;
  pageSize: number;
}

type RawParams = Record<string, string | string[] | undefined>;

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const MONEY = /^\d{1,13}(\.\d{1,2})?$/;

function all(params: RawParams, key: string): string[] {
  const value = params[key];
  return (Array.isArray(value) ? value : value === undefined ? [] : [value]).map((v) => v.trim()).filter(Boolean);
}

function first(params: RawParams, key: string): string {
  return all(params, key)[0] ?? "";
}

function positiveInt(value: string): number | null {
  if (!/^\d{1,9}$/.test(value)) return null;
  const n = Number(value);
  return n >= 1 ? n : null;
}

/** Wrong-type values are dropped (a MalformedRequest, F-25); range and length rules are the API's. */
export function parsePaymentFilters(params: RawParams): PaymentFilters {
  const status = all(params, "status");
  const method = first(params, "paymentMethod");
  const orderId = first(params, "orderId");
  const from = first(params, "from");
  const to = first(params, "to");
  const minAmount = first(params, "minAmount");
  const maxAmount = first(params, "maxAmount");
  const pageSize = positiveInt(first(params, "pageSize"));
  return {
    status: PAYMENT_STATUSES.filter((s) => status.includes(s)),
    paymentMethod: (PAYMENT_METHODS as string[]).includes(method) ? (method as PaymentMethod) : "",
    orderId: GUID.test(orderId) ? orderId : "",
    // Exact and case-sensitive at the API: passed through as typed.
    userId: first(params, "userId"),
    from: DATE.test(from) ? from : "",
    to: DATE.test(to) ? to : "",
    minAmount: MONEY.test(minAmount) ? minAmount : "",
    maxAmount: MONEY.test(maxAmount) ? maxAmount : "",
    pageNumber: positiveInt(first(params, "pageNumber")) ?? 1,
    pageSize: pageSize && pageSize <= 100 ? pageSize : DEFAULT_PAGE_SIZE,
  };
}

/** The filters shared by the list and the export; `to` is the END of that day. */
export function toPaymentFilterQuery(filters: PaymentFilters): PaymentFilterQuery {
  return {
    status: filters.status.length > 0 ? filters.status : undefined,
    paymentMethod: filters.paymentMethod || undefined,
    orderId: filters.orderId || undefined,
    userId: filters.userId || undefined,
    from: startOfDayUtc(filters.from),
    to: endOfDayUtc(filters.to),
    minAmount: filters.minAmount ? Number(filters.minAmount) : undefined,
    maxAmount: filters.maxAmount ? Number(filters.maxAmount) : undefined,
  };
}

export function toPaymentListQuery(filters: PaymentFilters): AdminPaymentListQuery {
  return { ...toPaymentFilterQuery(filters), pageNumber: filters.pageNumber, pageSize: filters.pageSize };
}

export function toLinkParams(filters: PaymentFilters): Record<string, QueryValue> {
  return {
    status: filters.status,
    paymentMethod: filters.paymentMethod,
    orderId: filters.orderId,
    userId: filters.userId,
    from: filters.from,
    to: filters.to,
    minAmount: filters.minAmount,
    maxAmount: filters.maxAmount,
    pageSize: filters.pageSize === DEFAULT_PAGE_SIZE ? undefined : filters.pageSize,
  };
}

export function hasActiveFilters(filters: PaymentFilters): boolean {
  return Boolean(
    filters.status.length ||
      filters.paymentMethod ||
      filters.orderId ||
      filters.userId ||
      filters.from ||
      filters.to ||
      filters.minAmount ||
      filters.maxAmount,
  );
}

/** "offline:<reference>" is a bank transfer or cash recorded by an admin (payment.md "offline"). */
export function methodLabel(method: PaymentMethod, intentId: string | null): string {
  if (method === "Mock") return intentId?.startsWith("offline:") ? "Offline" : "Simulator";
  return method === "None" ? "None (placeholder)" : "Card (Stripe)";
}
