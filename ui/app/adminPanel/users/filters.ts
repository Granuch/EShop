import type { QueryValue } from "@/lib/admin/href";
import type { AdminUserListQuery, AdminUserSortBy } from "@/lib/admin/types/identity";

export const USERS_PATH = "/adminPanel/users";
export const DEFAULT_PAGE_SIZE = 20;

export const SORTS = {
  newest: { label: "Newest first", sortBy: "CreatedAt", isDescending: true },
  oldest: { label: "Oldest first", sortBy: "CreatedAt", isDescending: false },
  email: { label: "Email A–Z", sortBy: "Email", isDescending: false },
  login: { label: "Last sign-in, newest", sortBy: "LastLoginAt", isDescending: true },
} as const satisfies Record<string, { label: string; sortBy: AdminUserSortBy; isDescending: boolean }>;

export type SortKey = keyof typeof SORTS;

/** One select over isActive / isDeleted: the API lists only deleted users when isDeleted=true. */
export const STATES = {
  "": "Live (active and inactive)",
  active: "Active",
  inactive: "Deactivated",
  deleted: "Deleted",
} as const;

export type StateKey = keyof typeof STATES;
type Flag = "" | "true" | "false";

export interface UserFilters {
  search: string;
  role: string;
  state: StateKey;
  emailConfirmed: Flag;
  twoFactorEnabled: Flag;
  sort: SortKey;
  pageNumber: number;
  pageSize: number;
}

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

const flag = (value: string): Flag => (value === "true" || value === "false" ? value : "");

/** Wrong-type values are dropped (a bad boolean would be a 400 blaming the parameter); lengths are the API's. */
export function parseUserFilters(params: RawParams): UserFilters {
  const state = first(params, "state");
  const sort = first(params, "sort");
  const pageSize = positiveInt(first(params, "pageSize"));
  return {
    search: first(params, "search"),
    role: first(params, "role"),
    state: state in STATES ? (state as StateKey) : "",
    emailConfirmed: flag(first(params, "emailConfirmed")),
    twoFactorEnabled: flag(first(params, "twoFactorEnabled")),
    sort: sort in SORTS ? (sort as SortKey) : "newest",
    pageNumber: positiveInt(first(params, "pageNumber")) ?? 1,
    pageSize: pageSize && pageSize <= 100 ? pageSize : DEFAULT_PAGE_SIZE,
  };
}

export function toUserListQuery(filters: UserFilters): AdminUserListQuery {
  const sort = SORTS[filters.sort];
  return {
    search: filters.search || undefined,
    role: filters.role || undefined,
    isActive: filters.state === "active" ? true : filters.state === "inactive" ? false : undefined,
    isDeleted: filters.state === "deleted" ? true : undefined,
    emailConfirmed: filters.emailConfirmed ? filters.emailConfirmed === "true" : undefined,
    twoFactorEnabled: filters.twoFactorEnabled ? filters.twoFactorEnabled === "true" : undefined,
    sortBy: sort.sortBy,
    isDescending: sort.isDescending,
    pageNumber: filters.pageNumber,
    pageSize: filters.pageSize,
  };
}

export function toLinkParams(filters: UserFilters): Record<string, QueryValue> {
  return {
    search: filters.search,
    role: filters.role,
    state: filters.state,
    emailConfirmed: filters.emailConfirmed,
    twoFactorEnabled: filters.twoFactorEnabled,
    sort: filters.sort === "newest" ? undefined : filters.sort,
    pageSize: filters.pageSize === DEFAULT_PAGE_SIZE ? undefined : filters.pageSize,
  };
}

export function hasActiveFilters(filters: UserFilters): boolean {
  return Boolean(filters.search || filters.role || filters.state || filters.emailConfirmed || filters.twoFactorEnabled);
}
