import type { QueryValue } from "@/lib/admin/href";
import type { ProductListQuery, ProductSortBy, ProductStatus } from "@/lib/admin/types/catalog";

export const PRODUCTS_PATH = "/adminPanel/products";
export const DEFAULT_PAGE_SIZE = 20;

/** One select for sort field + direction. "newest" is the admin default (the API's own default is Name). */
export const SORTS = {
  newest: { label: "Newest first", sortBy: "CreatedAt", isDescending: true },
  oldest: { label: "Oldest first", sortBy: "CreatedAt", isDescending: false },
  name: { label: "Name A–Z", sortBy: "Name", isDescending: false },
  name_desc: { label: "Name Z–A", sortBy: "Name", isDescending: true },
  price: { label: "Price: low to high", sortBy: "Price", isDescending: false },
  price_desc: { label: "Price: high to low", sortBy: "Price", isDescending: true },
} as const satisfies Record<string, { label: string; sortBy: ProductSortBy; isDescending: boolean }>;

export type SortKey = keyof typeof SORTS;

/** Discontinued is left out: it only ever matches deleted products, which this list never shows. */
export const STATUS_FILTERS: ProductStatus[] = ["Draft", "Active"];

/** The filter values as the form shows them, and as links carry them (URL names = API names where they exist). */
export interface ProductFilters {
  searchTerm: string;
  status: ProductStatus | "";
  categoryId: string;
  hasDiscount: "true" | "false" | "";
  stockBelow: string;
  sort: SortKey;
  pageNumber: number;
  pageSize: number;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

type RawParams = Record<string, string | string[] | undefined>;

function first(params: RawParams, key: string): string {
  const value = params[key];
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

function positiveInt(value: string): number | null {
  if (!/^\d{1,9}$/.test(value)) return null;
  const n = Number(value);
  return n >= 1 ? n : null;
}

/**
 * Reads the URL leniently: a value the API would refuse as a wrong type (MalformedRequest, F-25) is dropped here.
 * The search term is passed through as typed, so a one-character search reaches the API and its 400 is shown.
 */
export function parseProductFilters(params: RawParams): ProductFilters {
  const status = first(params, "status");
  const hasDiscount = first(params, "hasDiscount");
  const sort = first(params, "sort");
  const categoryId = first(params, "categoryId");
  const stockBelow = positiveInt(first(params, "stockBelow"));
  const pageSize = positiveInt(first(params, "pageSize"));

  return {
    searchTerm: first(params, "searchTerm"),
    status: (STATUS_FILTERS as string[]).includes(status) ? (status as ProductStatus) : "",
    categoryId: GUID.test(categoryId) ? categoryId : "",
    hasDiscount: hasDiscount === "true" || hasDiscount === "false" ? hasDiscount : "",
    stockBelow: stockBelow ? String(stockBelow) : "",
    sort: sort in SORTS ? (sort as SortKey) : "newest",
    pageNumber: positiveInt(first(params, "pageNumber")) ?? 1,
    pageSize: pageSize && pageSize <= 100 ? pageSize : DEFAULT_PAGE_SIZE,
  };
}

/** The documented query; only set fields are sent. */
export function toProductListQuery(filters: ProductFilters): ProductListQuery {
  const sort = SORTS[filters.sort];
  return {
    pageNumber: filters.pageNumber,
    pageSize: filters.pageSize,
    searchTerm: filters.searchTerm || undefined,
    status: filters.status || undefined,
    categoryId: filters.categoryId || undefined,
    hasDiscount: filters.hasDiscount ? filters.hasDiscount === "true" : undefined,
    stockBelow: filters.stockBelow ? Number(filters.stockBelow) : undefined,
    sortBy: sort.sortBy,
    isDescending: sort.isDescending,
  };
}

/** Link params for paging: every filter, without pageNumber; defaults are left out of the URL. */
export function toLinkParams(filters: ProductFilters): Record<string, QueryValue> {
  return {
    searchTerm: filters.searchTerm,
    status: filters.status,
    categoryId: filters.categoryId,
    hasDiscount: filters.hasDiscount,
    stockBelow: filters.stockBelow,
    sort: filters.sort === "newest" ? undefined : filters.sort,
    pageSize: filters.pageSize === DEFAULT_PAGE_SIZE ? undefined : filters.pageSize,
  };
}

export function hasActiveFilters(filters: ProductFilters): boolean {
  return Boolean(filters.searchTerm || filters.status || filters.categoryId || filters.hasDiscount || filters.stockBelow);
}
