import { endOfDayUtc, startOfDayUtc } from "@/lib/admin/format";
import type { QueryValue } from "@/lib/admin/href";
import type {
  NotificationFilterQuery,
  NotificationListQuery,
  NotificationStatus,
  NotificationTemplateName,
} from "@/lib/admin/types/notification";

export const NOTIFICATIONS_PATH = "/adminPanel/notifications";
export const DEFAULT_PAGE_SIZE = 20;

/** In delivery order; the API's own byStatus order is Pending, Sent, Failed, Sending, Undeliverable. */
export const NOTIFICATION_STATUSES: NotificationStatus[] = ["Pending", "Sending", "Sent", "Failed", "Undeliverable"];

export const TEMPLATE_NAMES: NotificationTemplateName[] = [
  "order-created",
  "order-shipped",
  "payment-created",
  "payment-completed",
  "payment-failed",
  "payment-refunded",
  "password-reset",
  "email-confirmation",
];

export interface NotificationFilters {
  status: NotificationStatus[];
  templateName: NotificationTemplateName | "";
  email: string;
  userId: string;
  from: string;
  to: string;
  hasError: "" | "true" | "false";
  pageNumber: number;
  pageSize: number;
}

type RawParams = Record<string, string | string[] | undefined>;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

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

/** Wrong-type values are dropped (a MalformedRequest, F-25); exact-match lengths are the API's to check. */
export function parseNotificationFilters(params: RawParams): NotificationFilters {
  const status = all(params, "status");
  const template = first(params, "templateName");
  const from = first(params, "from");
  const to = first(params, "to");
  const hasError = first(params, "hasError");
  const pageSize = positiveInt(first(params, "pageSize"));
  return {
    status: NOTIFICATION_STATUSES.filter((s) => status.includes(s)),
    templateName: (TEMPLATE_NAMES as string[]).includes(template) ? (template as NotificationTemplateName) : "",
    email: first(params, "email"),
    userId: first(params, "userId"),
    from: DATE.test(from) ? from : "",
    to: DATE.test(to) ? to : "",
    hasError: hasError === "true" || hasError === "false" ? hasError : "",
    pageNumber: positiveInt(first(params, "pageNumber")) ?? 1,
    pageSize: pageSize && pageSize <= 100 ? pageSize : DEFAULT_PAGE_SIZE,
  };
}

/** The filters the journal and the statistics share; `to` is the END of that day. */
export function toNotificationFilterQuery(filters: NotificationFilters): NotificationFilterQuery {
  return {
    status: filters.status.length > 0 ? filters.status : undefined,
    templateName: filters.templateName || undefined,
    email: filters.email || undefined,
    userId: filters.userId || undefined,
    from: startOfDayUtc(filters.from),
    to: endOfDayUtc(filters.to),
    hasError: filters.hasError ? filters.hasError === "true" : undefined,
  };
}

export function toNotificationListQuery(filters: NotificationFilters): NotificationListQuery {
  return { ...toNotificationFilterQuery(filters), pageNumber: filters.pageNumber, pageSize: filters.pageSize };
}

export function toLinkParams(filters: NotificationFilters): Record<string, QueryValue> {
  return {
    status: filters.status,
    templateName: filters.templateName,
    email: filters.email,
    userId: filters.userId,
    from: filters.from,
    to: filters.to,
    hasError: filters.hasError,
    pageSize: filters.pageSize === DEFAULT_PAGE_SIZE ? undefined : filters.pageSize,
  };
}

export function hasActiveFilters(filters: NotificationFilters): boolean {
  return Boolean(
    filters.status.length || filters.templateName || filters.email || filters.userId || filters.from || filters.to || filters.hasError,
  );
}
