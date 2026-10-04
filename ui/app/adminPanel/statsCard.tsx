import Link from "next/link";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { classifyFailure } from "@/lib/admin/api";
import { formatDate, formatMoney } from "@/lib/admin/format";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { OrderStats } from "@/lib/admin/types/ordering";
import { DAYS, loadOrderStats } from "./dashboardData";

function Shell({ children, description }: { children: React.ReactNode; description?: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle id="stats-heading">Orders, last {DAYS} days</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

function Kpi({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-lg font-semibold tabular-nums">{value}</dd>
    </div>
  );
}

export function StatsCardSkeleton() {
  return (
    <Shell>
      <div aria-busy="true" className="space-y-3">
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-24 w-full" />
      </div>
    </Shell>
  );
}

/** Totals and the per-status breakdown, as the API computes them (never recomputed here). Never throws. */
export default async function StatsCard() {
  let stats: OrderStats;
  try {
    stats = await loadOrderStats();
  } catch (error) {
    const failure = classifyFailure(error);
    const message =
      failure?.kind === "forbidden"
        ? `This account may not read order statistics. ${ADMIN_ROLE_HINTS.ordering}`
        : failure?.kind === "rateLimited" || failure?.kind === "invalid"
          ? failure.message
          : "The ordering service could not be reached.";
    return (
      <Shell>
        <p role="alert" className="text-sm text-destructive">
          Could not load order statistics. {message}
        </p>
      </Shell>
    );
  }

  return (
    <Shell description={`${formatDate(stats.from)} to ${formatDate(stats.to)} (UTC)`}>
      <dl className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Kpi label="Orders" value={String(stats.totalOrders)} />
        <Kpi label="Paid revenue" value={formatMoney(stats.paidRevenue)} />
        <Kpi label="Gross value" value={formatMoney(stats.grossValue)} />
        <Kpi label="Refunded" value={formatMoney(stats.refundedValue)} />
      </dl>
      <p className="mt-1 text-xs text-muted-foreground">
        Paid revenue counts Paid, Shipped and Delivered orders; gross value counts every status.
      </p>
      <h3 className="mt-6 text-xs font-medium text-muted-foreground">By status</h3>
      <ul className="mt-2 divide-y">
        {stats.byStatus.map((entry) => (
          <li key={entry.status} className="flex items-center justify-between py-1.5 text-sm">
            <Link href={`/adminPanel/orders?statuses=${entry.status}`} className="hover:underline">
              {entry.status}
            </Link>
            <span className="tabular-nums text-muted-foreground">
              {entry.count} · {formatMoney(entry.value)}
            </span>
          </li>
        ))}
      </ul>
    </Shell>
  );
}
