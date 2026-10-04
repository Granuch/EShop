import Link from "next/link";
import StatusBadge, { ORDER_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { classifyFailure } from "@/lib/admin/api";
import { formatDateTime, formatMoney } from "@/lib/admin/format";
import { listOrders } from "@/lib/admin/ordering";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { PagedResult } from "@/lib/admin/types/common";
import type { Order } from "@/lib/admin/types/ordering";

const SHOWN = 8;

/** "Oct 3, 22:57": the card says UTC once, and a full timestamp does not fit beside the status on a phone. */
const shortTime = (value: string) =>
  new Date(value).toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
    timeZone: "UTC",
  });

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle id="recent-orders-heading">Recent orders</CardTitle>
        <CardDescription>Newest first, times in UTC</CardDescription>
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

export function RecentOrdersCardSkeleton() {
  return (
    <Shell>
      <div aria-busy="true" className="space-y-2">
        {Array.from({ length: SHOWN }, (_, i) => (
          <Skeleton key={i} className="h-6 w-full" />
        ))}
      </div>
    </Shell>
  );
}

/** The newest orders, each linking to its page; ids shortened as in the orders list (PLAN Q12). Never throws. */
export default async function RecentOrdersCard() {
  let page: PagedResult<Order>;
  try {
    page = await listOrders({ pageSize: SHOWN, sortBy: "CreatedAt", isDescending: true });
  } catch (error) {
    const failure = classifyFailure(error);
    const message =
      failure?.kind === "forbidden"
        ? `This account may not read orders. ${ADMIN_ROLE_HINTS.ordering}`
        : failure?.kind === "rateLimited" || failure?.kind === "invalid"
          ? failure.message
          : "The ordering service could not be reached.";
    return (
      <Shell>
        <p role="alert" className="text-sm text-destructive">
          Could not load orders. {message}
        </p>
      </Shell>
    );
  }

  if (page.items.length === 0) {
    return (
      <Shell>
        <p className="text-sm text-muted-foreground">No orders yet.</p>
      </Shell>
    );
  }

  return (
    <Shell>
      <ul className="divide-y">
        {page.items.map((order) => (
          <li key={order.id} className="flex items-center justify-between gap-3 py-1.5 text-sm">
            <span className="flex min-w-0 items-baseline gap-2">
              <Link href={`/adminPanel/orders/${order.id}`} className="font-mono text-xs font-medium hover:underline" title={order.id}>
                {order.id.slice(0, 8)}
              </Link>
              <span className="truncate text-xs text-muted-foreground" title={formatDateTime(order.createdAt)}>
                {shortTime(order.createdAt)}
              </span>
            </span>
            <span className="flex shrink-0 items-center gap-2">
              <StatusBadge status={order.status} tone={ORDER_STATUS_TONES[order.status]} />
              <span className="w-20 text-right tabular-nums">{formatMoney(order.totalPrice)}</span>
            </span>
          </li>
        ))}
      </ul>
      <Link href="/adminPanel/orders" className="mt-3 inline-block text-sm underline">
        {page.totalCount > SHOWN ? `View all ${page.totalCount}` : "View in orders"}
      </Link>
    </Shell>
  );
}
