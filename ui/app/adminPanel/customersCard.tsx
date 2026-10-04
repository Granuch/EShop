import Link from "next/link";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminUserStats } from "@/lib/admin/identity";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { AdminUserStats } from "@/lib/admin/types/identity";
import { DAYS, statsWindow } from "./dashboardData";

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle id="customers-heading">Customers</CardTitle>
        <CardDescription>All accounts, admins included</CardDescription>
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

function Kpi({ label, value, href }: { label: string; value: number; href?: string }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-lg font-semibold tabular-nums">
        {href ? (
          <Link href={href} className="hover:underline">
            {value}
          </Link>
        ) : (
          value
        )}
      </dd>
    </div>
  );
}

export function CustomersCardSkeleton() {
  return (
    <Shell>
      <Skeleton aria-busy="true" className="h-12 w-full" />
    </Shell>
  );
}

/** Account counts from Identity's user stats; "new" covers the same 30 days as the order cards. Never throws. */
export default async function CustomersCard() {
  let stats: AdminUserStats;
  try {
    stats = await getAdminUserStats(statsWindow());
  } catch (error) {
    const failure = classifyFailure(error);
    const message =
      failure?.kind === "forbidden"
        ? `This account may not read user statistics. ${ADMIN_ROLE_HINTS.identity}`
        : failure?.kind === "rateLimited" || failure?.kind === "invalid"
          ? failure.message
          : "The identity service could not be reached.";
    return (
      <Shell>
        <p role="alert" className="text-sm text-destructive">
          Could not load customer statistics. {message}
        </p>
      </Shell>
    );
  }

  return (
    <Shell>
      <dl className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Kpi label="Accounts" value={stats.total} href="/adminPanel/users" />
        <Kpi label={`New, ${DAYS} days`} value={stats.newInPeriod} href="/adminPanel/users?sort=newest" />
        <Kpi label="Email unconfirmed" value={stats.unconfirmed} href="/adminPanel/users?emailConfirmed=false" />
        <Kpi label="Locked out" value={stats.locked} />
      </dl>
    </Shell>
  );
}
