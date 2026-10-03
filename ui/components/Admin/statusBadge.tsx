import { Badge } from "@/components/ui/badge";

export type StatusTone = "neutral" | "success" | "warning" | "danger";

const TONE_CLASSES: Record<StatusTone, string> = {
  neutral: "border-border bg-muted text-muted-foreground",
  success: "bg-emerald-500/10 text-emerald-700 dark:text-emerald-400",
  warning: "bg-amber-500/10 text-amber-700 dark:text-amber-400",
  danger: "bg-destructive/10 text-destructive",
};

/** Product status → tone. Discontinued means "deleted" (catalog.md "Product lifecycle"). */
export const PRODUCT_STATUS_TONES = {
  Draft: "neutral",
  Active: "success",
  Discontinued: "danger",
} as const satisfies Record<string, StatusTone>;

/** Order status → tone. Paid, Shipped and Delivered are the healthy path; Pending waits on Payment. */
export const ORDER_STATUS_TONES = {
  Pending: "warning",
  Paid: "success",
  Shipped: "success",
  Delivered: "success",
  Cancelled: "neutral",
  Refunded: "danger",
} as const satisfies Record<string, StatusTone>;

/** Payment status → tone. Success is the money taken; Processing/Pending wait on the customer or Stripe. */
export const PAYMENT_STATUS_TONES = {
  Pending: "warning",
  Processing: "warning",
  Success: "success",
  Failed: "danger",
  Refunded: "danger",
  Cancelled: "neutral",
} as const satisfies Record<string, StatusTone>;

/** An enum name shown as sent (PascalCase), coloured by its domain's tone map. */
function StatusBadge({ status, tone }: { status: string; tone: StatusTone }) {
  return (
    <Badge variant="outline" className={TONE_CLASSES[tone]}>
      {status}
    </Badge>
  );
}

export default StatusBadge;
