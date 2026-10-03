import { Suspense } from "react";
import Link from "next/link";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import { getAdminSession, hasAnyPermission, hasPermission } from "@/lib/admin/auth";
import { visibleSections } from "@/lib/admin/permissions";
import HealthCard, { HealthCardSkeleton } from "./healthCard";
import StatsCard, { StatsCardSkeleton } from "./statsCard";

const DASHBOARD = "/adminPanel";

export default async function AdminDashboard() {
  // Checked here as well as in the layout: a layout is not an auth boundary.
  const session = await getAdminSession();
  if (!hasAnyPermission(session)) {
    return <AccessDenied fullPage title="No admin access" message="This account has no admin access." />;
  }

  const sections = visibleSections(session.permissions).filter((section) => section.href !== DASHBOARD);
  const canSeeHealth = hasPermission(session, "system.manage");
  const canSeeOrders = hasPermission(session, "orders.read");

  return (
    <div className="space-y-8">
      <PageHeader title="Dashboard" description={`Signed in as ${session.email}`} />

      {sections.length > 0 && (
        <section aria-labelledby="sections-heading">
          <h2 id="sections-heading" className="text-sm font-semibold">
            Sections
          </h2>
          <ul className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {sections.map((section) => (
              <li key={section.href}>
                <Link
                  href={section.href}
                  className="block rounded-xl px-4 py-3 text-sm font-medium ring-1 ring-foreground/10 transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  {section.label}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* Each card streams on its own, so one slow service (health waits up to 5 s) does not hold the others. */}
      <div className="grid items-start gap-6 xl:grid-cols-2">
        {canSeeOrders && (
          <section aria-labelledby="stats-heading">
            <Suspense fallback={<StatsCardSkeleton />}>
              <StatsCard />
            </Suspense>
          </section>
        )}
        {canSeeHealth && (
          <section aria-labelledby="health-heading">
            <Suspense fallback={<HealthCardSkeleton />}>
              <HealthCard />
            </Suspense>
          </section>
        )}
      </div>

      {sections.length === 0 && !canSeeHealth && !canSeeOrders && (
        <p className="text-sm text-muted-foreground">Nothing on the dashboard for this account yet.</p>
      )}
    </div>
  );
}
