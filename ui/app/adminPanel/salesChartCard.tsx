import Link from "next/link";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { classifyFailure } from "@/lib/admin/api";
import { formatMoney } from "@/lib/admin/format";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { OrderStats } from "@/lib/admin/types/ordering";
import { DAYS, loadOrderStats, statsWindow, windowDays } from "./dashboardData";

interface Day {
  date: string;
  revenue: number;
  orders: number;
}

const shortDate = (date: string) =>
  new Date(`${date}T00:00:00Z`).toLocaleDateString("en-US", { month: "short", day: "numeric", timeZone: "UTC" });

function Shell({ children, description }: { children: React.ReactNode; description?: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle id="sales-heading">Paid revenue per day</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

export function SalesChartCardSkeleton() {
  return (
    <Shell>
      <Skeleton aria-busy="true" className="h-44 w-full" />
    </Shell>
  );
}

/** Where a bar's tooltip sits, so the first and last few do not spill out of the card. */
function tooltipPosition(index: number): string {
  if (index < 5) return "left-0";
  if (index >= DAYS - 5) return "right-0";
  return "left-1/2 -translate-x-1/2";
}

/**
 * One bar per UTC day of the last 30, from the same stats response as the orders card (no second request). The API
 * returns non-empty days only, so the others are filled with zero here. Hover shows the day; a hidden table carries
 * the same numbers for screen readers. Never throws.
 */
export default async function SalesChartCard() {
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
          Could not load the chart. {message}
        </p>
      </Shell>
    );
  }

  const byDate = new Map(stats.buckets.map((bucket) => [bucket.periodStart.slice(0, 10), bucket]));
  const days: Day[] = windowDays(stats.from ?? statsWindow().from).map((date) => ({
    date,
    revenue: byDate.get(date)?.paidRevenue ?? 0,
    orders: byDate.get(date)?.orderCount ?? 0,
  }));
  const peak = days.reduce((best, day) => (day.revenue > best.revenue ? day : best), days[0]);

  if (peak.revenue === 0) {
    return (
      <Shell description={`Last ${DAYS} days (UTC)`}>
        <p className="text-sm text-muted-foreground">No paid orders in the last {DAYS} days.</p>
      </Shell>
    );
  }

  return (
    <Shell description={`${formatMoney(stats.paidRevenue)} over the last ${DAYS} days (UTC)`}>
      <p className="text-xs text-muted-foreground">{`Peak ${formatMoney(peak.revenue)} on ${shortDate(peak.date)}`}</p>
      <div aria-hidden className="mt-2 flex h-40 items-end gap-0.5 border-b border-border">
        {days.map((day, index) => (
          <div key={day.date} className="group relative flex h-full flex-1 items-end">
            {day.revenue > 0 && (
              <div
                className="w-full rounded-t-[4px] bg-primary/70 transition-colors group-hover:bg-primary"
                style={{ height: `${Math.max((day.revenue / peak.revenue) * 100, 2)}%` }}
              />
            )}
            <div
              className={`pointer-events-none absolute bottom-full z-10 mb-1 hidden whitespace-nowrap rounded-md bg-popover px-2 py-1 text-xs text-popover-foreground shadow-md ring-1 ring-foreground/10 group-hover:block ${tooltipPosition(index)}`}
            >
              <span className="font-medium">{shortDate(day.date)}</span>
              {` · ${formatMoney(day.revenue)} paid · ${day.orders} ${day.orders === 1 ? "order" : "orders"} placed`}
            </div>
          </div>
        ))}
      </div>
      <div aria-hidden className="mt-1 flex justify-between text-xs text-muted-foreground">
        <span>{shortDate(days[0].date)}</span>
        <span>{shortDate(days[days.length - 1].date)}</span>
      </div>

      <table className="sr-only">
        <caption>Paid revenue and orders per day, last {DAYS} days (UTC)</caption>
        <thead>
          <tr>
            <th scope="col">Day</th>
            <th scope="col">Paid revenue</th>
            <th scope="col">Orders</th>
          </tr>
        </thead>
        <tbody>
          {days.map((day) => (
            <tr key={day.date}>
              <th scope="row">{day.date}</th>
              <td>{formatMoney(day.revenue)}</td>
              <td>{day.orders}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <p className="mt-3 text-xs text-muted-foreground">
        Bars count Paid, Shipped and Delivered orders by the day they were placed.{" "}
        <Link href="/adminPanel/orders" className="underline">
          Orders
        </Link>
      </p>
    </Shell>
  );
}
