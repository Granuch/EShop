import { Suspense } from "react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import { getAdminSession, hasAnyPermission, hasPermission } from "@/lib/admin/auth";
import CustomersCard, { CustomersCardSkeleton } from "./customersCard";
import HealthCard, { HealthCardSkeleton } from "./healthCard";
import LowStockCard, { LowStockCardSkeleton } from "./lowStockCard";
import NeedsAttentionCard, { NeedsAttentionCardSkeleton, visibleQueues } from "./needsAttentionCard";
import RecentOrdersCard, { RecentOrdersCardSkeleton } from "./recentOrdersCard";
import SalesChartCard, { SalesChartCardSkeleton } from "./salesChartCard";
import StatsCard, { StatsCardSkeleton } from "./statsCard";

export default async function AdminDashboard() {
  // Checked here as well as in the layout: a layout is not an auth boundary.
  const session = await getAdminSession();
  if (!hasAnyPermission(session)) {
    return <AccessDenied fullPage title="No admin access" message="This account has no admin access." />;
  }

  // The sidebar lists every section, so the dashboard shows only data. Each card needs the permission it reads.
  const queues = visibleQueues(session).length;
  const canSeeOrders = hasPermission(session, "orders.read");
  const canSeeStock = hasPermission(session, "catalog.read");
  const canSeeUsers = hasPermission(session, "users.read");
  const canSeeHealth = hasPermission(session, "system.manage");
  const nothing = queues === 0 && !canSeeOrders && !canSeeStock && !canSeeUsers && !canSeeHealth;

  return (
    <div className="space-y-6">
      <PageHeader title="Dashboard" description={`Signed in as ${session.email}`} />

      {/* Each card streams on its own, so one slow service (health waits up to 5 s) does not hold the others. */}
      {queues > 0 && (
        <section aria-labelledby="attention-heading">
          <Suspense fallback={<NeedsAttentionCardSkeleton tiles={queues} />}>
            <NeedsAttentionCard session={session} />
          </Suspense>
        </section>
      )}

      <div className="grid items-start gap-6 xl:grid-cols-2">
        {canSeeOrders && (
          <section aria-labelledby="stats-heading" className="min-w-0">
            <Suspense fallback={<StatsCardSkeleton />}>
              <StatsCard />
            </Suspense>
          </section>
        )}
        {canSeeOrders && (
          <section aria-labelledby="sales-heading" className="min-w-0">
            <Suspense fallback={<SalesChartCardSkeleton />}>
              <SalesChartCard />
            </Suspense>
          </section>
        )}
        {canSeeOrders && (
          <section aria-labelledby="recent-orders-heading" className="min-w-0">
            <Suspense fallback={<RecentOrdersCardSkeleton />}>
              <RecentOrdersCard />
            </Suspense>
          </section>
        )}
        {canSeeStock && (
          <section aria-labelledby="low-stock-heading" className="min-w-0">
            <Suspense fallback={<LowStockCardSkeleton />}>
              <LowStockCard />
            </Suspense>
          </section>
        )}
        {canSeeUsers && (
          <section aria-labelledby="customers-heading" className="min-w-0">
            <Suspense fallback={<CustomersCardSkeleton />}>
              <CustomersCard />
            </Suspense>
          </section>
        )}
        {canSeeHealth && (
          <section aria-labelledby="health-heading" className="min-w-0">
            <Suspense fallback={<HealthCardSkeleton />}>
              <HealthCard />
            </Suspense>
          </section>
        )}
      </div>

      {nothing && <p className="text-sm text-muted-foreground">Nothing on the dashboard for this account yet.</p>}
    </div>
  );
}
