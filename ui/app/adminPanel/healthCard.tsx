import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/admin/api";
import { formatDateTime } from "@/lib/admin/format";
import { getSystemHealth } from "@/lib/admin/platform";
import type { HealthStatus, SystemComponent, SystemHealth } from "@/lib/admin/types/platform";

const COMPONENT_LABELS: Record<SystemComponent, string> = {
  gateway: "Gateway",
  identity: "Identity",
  catalog: "Catalog",
  basket: "Basket",
  ordering: "Ordering",
  payment: "Payment",
  notification: "Notification",
};

const STATUS_STYLES: Record<HealthStatus, string> = {
  Healthy: "bg-emerald-500/10 text-emerald-700 dark:text-emerald-400",
  Degraded: "bg-amber-500/10 text-amber-700 dark:text-amber-400",
  Unhealthy: "bg-destructive/10 text-destructive",
};

function HealthBadge({ status, label }: { status: HealthStatus; label?: string }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_STYLES[status]}`}>
      {label ?? status}
    </span>
  );
}

function HealthShell({ description, children }: { description?: React.ReactNode; children: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle id="health-heading">System health</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

function describeFailure(error: unknown): string {
  if (!(error instanceof ApiError)) return "The API gateway could not be reached.";
  if (error.status === 403) return "This account may not read system health (system.manage).";
  if (error.status === 429) {
    return `Too many requests — try again in ${error.retryAfter ?? 60} s.`;
  }
  return `The gateway answered ${error.status}${error.errorCode ? ` (${error.errorCode})` : ""}.`;
}

export function HealthCardSkeleton() {
  return (
    <HealthShell description="Asking every service… this can take a few seconds.">
      <div aria-busy="true" className="space-y-2">
        {Array.from({ length: 7 }, (_, i) => (
          <Skeleton key={i} className="h-6 w-full" />
        ))}
      </div>
    </HealthShell>
  );
}

/** The Q6 health card. Never throws: a failure is shown inside the card, not handed to error.tsx. */
export default async function HealthCard() {
  let health: SystemHealth;
  try {
    health = await getSystemHealth();
  } catch (error) {
    return (
      <HealthShell>
        <p role="alert" className="text-sm text-destructive">
          Could not load system health. {describeFailure(error)}
        </p>
      </HealthShell>
    );
  }

  return (
    <HealthShell
      description={
        <span className="flex flex-wrap items-center gap-2">
          <HealthBadge status={health.status} />
          <span>Checked {formatDateTime(health.checkedAt)}</span>
        </span>
      }
    >
      <ul className="divide-y">
        {health.components.map((component) => {
          const failing = component.checks.filter((check) => check.status !== "Healthy");
          return (
            <li key={component.name} className="py-2 first:pt-0 last:pb-0">
              <div className="flex items-center justify-between gap-4">
                <span className="text-sm font-medium">{COMPONENT_LABELS[component.name] ?? component.name}</span>
                <HealthBadge
                  status={component.status}
                  label={component.reachable ? component.status : "Unreachable"}
                />
              </div>
              {failing.length > 0 && (
                <ul className="mt-1 space-y-0.5 text-xs text-muted-foreground">
                  {failing.map((check) => (
                    <li key={check.name}>
                      {check.name}: {check.status}
                    </li>
                  ))}
                </ul>
              )}
            </li>
          );
        })}
      </ul>
    </HealthShell>
  );
}
