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

/** An enum name shown as sent (PascalCase), coloured by its domain's tone map. */
function StatusBadge({ status, tone }: { status: string; tone: StatusTone }) {
  return (
    <Badge variant="outline" className={TONE_CLASSES[tone]}>
      {status}
    </Badge>
  );
}

export default StatusBadge;
