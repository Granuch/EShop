import Link from "next/link";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { classifyFailure } from "@/lib/admin/api";
import { hasPermission, type AdminSession } from "@/lib/admin/auth";
import { countDeadLetters } from "@/lib/admin/basket";
import { getNotificationStats } from "@/lib/admin/notification";
import { listOrders } from "@/lib/admin/ordering";
import { listPayments } from "@/lib/admin/payment";
import type { Permission } from "@/lib/admin/types/common";

interface Queue {
  label: string;
  hint: string;
  href: string;
  permission: Permission;
  count: () => Promise<number>;
}

/** Work an admin can act on, each counted by the service that owns it and linked to the matching list. */
const QUEUES: Queue[] = [
  {
    label: "Orders to ship",
    hint: "Paid, not shipped yet",
    href: "/adminPanel/orders?statuses=Paid",
    permission: "orders.read",
    count: async () => (await listOrders({ statuses: ["Paid"], pageSize: 1 })).totalCount,
  },
  {
    label: "Pending payments",
    hint: "Waiting for the customer or Stripe",
    href: "/adminPanel/payments?status=Pending",
    permission: "payments.read",
    count: async () => (await listPayments({ status: ["Pending"], pageSize: 1 })).totalCount,
  },
  {
    label: "Failed emails",
    hint: "Can be retried",
    href: "/adminPanel/notifications?status=Failed",
    permission: "notifications.read",
    count: async () => (await getNotificationStats({})).failed,
  },
  {
    label: "Basket dead letters",
    hint: "Checkout events that were not delivered",
    href: "/adminPanel/baskets/outbox",
    permission: "system.manage",
    count: async () => (await countDeadLetters()).count,
  },
];

export function visibleQueues(session: AdminSession): Queue[] {
  return QUEUES.filter((queue) => hasPermission(session, queue.permission));
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle id="attention-heading">Needs attention</CardTitle>
        <CardDescription>Counts as of now</CardDescription>
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

const GRID = "grid grid-cols-2 gap-3 xl:grid-cols-4";

export function NeedsAttentionCardSkeleton({ tiles }: { tiles: number }) {
  return (
    <Shell>
      <div aria-busy="true" className={GRID}>
        {Array.from({ length: tiles }, (_, i) => (
          <Skeleton key={i} className="h-20 w-full" />
        ))}
      </div>
    </Shell>
  );
}

/** One tile per queue the account may read; a queue that fails says so on its own tile. Never throws. */
export default async function NeedsAttentionCard({ session }: { session: AdminSession }) {
  const queues = visibleQueues(session);
  const results = await Promise.allSettled(queues.map((queue) => queue.count()));

  return (
    <Shell>
      <ul className={GRID}>
        {queues.map((queue, index) => {
          const result = results[index];
          const count = result.status === "fulfilled" ? result.value : null;
          const failure = result.status === "rejected" ? classifyFailure(result.reason) : null;
          return (
            <li key={queue.href}>
              <Link
                href={queue.href}
                className="block h-full rounded-xl px-4 py-3 ring-1 ring-foreground/10 transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              >
                <span
                  className={`block text-2xl font-semibold tabular-nums ${count ? "text-foreground" : "text-muted-foreground"}`}
                >
                  {count ?? "—"}
                </span>
                <span className="block text-sm font-medium">{queue.label}</span>
                {count === null ? (
                  <span className="block text-xs text-destructive">
                    {failure?.kind === "forbidden"
                      ? "Not allowed for this account"
                      : failure?.kind === "rateLimited"
                        ? failure.message
                        : "Could not be counted"}
                  </span>
                ) : (
                  <span className="block text-xs text-muted-foreground">{queue.hint}</span>
                )}
              </Link>
            </li>
          );
        })}
      </ul>
    </Shell>
  );
}
