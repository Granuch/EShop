import { endOfDayUtc, startOfDayUtc } from "@/lib/admin/format";
import type { QueryValue } from "@/lib/admin/href";
import type { AdminOrderListQuery, OrderSortBy, OrderStatus } from "@/lib/admin/types/ordering";

export const ORDERS_PATH = "/adminPanel/orders";
export const DEFAULT_PAGE_SIZE = 20;

/** In lifecycle order (the API's sortBy=Status is alphabetical instead, F-50). */
export const ORDER_STATUSES: OrderStatus[] = ["Pending", "Paid", "Shipped", "Delivered", "Cancelled", "Refunded"];

export const SORTS = {
  newest: { label: "Newest first", sortBy: "CreatedAt", isDescending: true },
  oldest: { label: "Oldest first", sortBy: "CreatedAt", isDescending: false },
  total_desc: { label: "Total: high to low", sortBy: "TotalPrice", isDescending: true },
  total: { label: "Total: low to high", sortBy: "TotalPrice", isDescending: false },
  status: { label: "Status (A–Z)", sortBy: "Status", isDescending: false },
} as const satisfies Record<string, { label: string; sortBy: OrderSortBy; isDescending: boolean }>;

export type SortKey = keyof typeof SORTS;

export interface OrderFilters {
  statuses: OrderStatus[];
  search: string;
  /** YYYY-MM-DD, as the date inputs hold them. */
  from: string;
  to: string;
  minTotal: string;
  maxTotal: string;
  sort: SortKey;
  pageNumber: number;
  pageSize: number;
}

type RawParams = Record<string, string | string[] | undefined>;

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

/** Wrong-type values are dropped (they would be a MalformedRequest, F-25); range rules are left to the API. */
export function parseOrderFilters(params: RawParams): OrderFilters {
  const statuses = all(params, "statuses").filter((s): s is OrderStatus => (ORDER_STATUSES as string[]).includes(s));
  const from = first(params, "from");
  const to = first(params, "to");
  const minTotal = first(params, "minTotal");
  const maxTotal = first(params, "maxTotal");
  const sort = first(params, "sort");
  const pageSize = positiveInt(first(params, "pageSize"));

  return {
    statuses: ORDER_STATUSES.filter((s) => statuses.includes(s)),
    search: first(params, "search"),
    from: DATE.test(from) ? from : "",
    to: DATE.test(to) ? to : "",
    minTotal: MONEY.test(minTotal) ? minTotal : "",
    maxTotal: MONEY.test(maxTotal) ? maxTotal : "",
    sort: sort in SORTS ? (sort as SortKey) : "newest",
    pageNumber: positiveInt(first(params, "pageNumber")) ?? 1,
    pageSize: pageSize && pageSize <= 100 ? pageSize : DEFAULT_PAGE_SIZE,
  };
}

/** Dates become inclusive UTC bounds: `to` is the END of that day (a bare date would mean its start, §10). */
export function toOrderListQuery(filters: OrderFilters): AdminOrderListQuery {
  const sort = SORTS[filters.sort];
  return {
    pageNumber: filters.pageNumber,
    pageSize: filters.pageSize,
    statuses: filters.statuses.length > 0 ? filters.statuses : undefined,
    search: filters.search || undefined,
    from: startOfDayUtc(filters.from),
    to: endOfDayUtc(filters.to),
    minTotal: filters.minTotal ? Number(filters.minTotal) : undefined,
    maxTotal: filters.maxTotal ? Number(filters.maxTotal) : undefined,
    sortBy: sort.sortBy,
    isDescending: sort.isDescending,
  };
}

export function toLinkParams(filters: OrderFilters): Record<string, QueryValue> {
  return {
    statuses: filters.statuses,
    search: filters.search,
    from: filters.from,
    to: filters.to,
    minTotal: filters.minTotal,
    maxTotal: filters.maxTotal,
    sort: filters.sort === "newest" ? undefined : filters.sort,
    pageSize: filters.pageSize === DEFAULT_PAGE_SIZE ? undefined : filters.pageSize,
  };
}

export function hasActiveFilters(filters: OrderFilters): boolean {
  return Boolean(
    filters.statuses.length || filters.search || filters.from || filters.to || filters.minTotal || filters.maxTotal,
  );
}
