import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import StatusBadge from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { formatDateTime } from "@/lib/admin/format";
import { getFeatureFlags, getSystemHealth, getSystemSettings } from "@/lib/admin/platform";
import type { HealthStatus } from "@/lib/admin/types/platform";
import { invalidateCacheAction } from "./actions";
import CacheForm from "./cacheForm";

export const metadata = { title: "System · Admin · EShop" };

const HEALTH_TONES = { Healthy: "success", Degraded: "warning", Unhealthy: "danger" } as const satisfies Record<HealthStatus, string>;

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-4 py-1 text-sm">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right">{children}</dd>
    </div>
  );
}

function Unavailable({ services }: { services: string[] }) {
  if (services.length === 0) return null;
  return <p className="text-xs text-destructive">{`Not reachable within 5 s: ${services.join(", ")}.`}</p>;
}

/** Health, settings and flags in full; the gateway asks every service at once, so this can take about 5 s. */
export default async function SystemPage() {
  const session = await getAdminSession();
  if (!hasPermission(session, "system.manage")) return <AccessDenied />;

  const [health, settings, flags] = await Promise.all([getSystemHealth(), getSystemSettings(), getFeatureFlags()]);

  return (
    <div className="space-y-6">
      <PageHeader title="System" description={`Checked ${formatDateTime(health.checkedAt)}. Read-only, apart from the catalog cache.`}>
        <StatusBadge status={health.status} tone={HEALTH_TONES[health.status]} />
      </PageHeader>

      {health.status !== "Healthy" && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>
            {`Not healthy: ${health.components.filter((c) => c.status !== "Healthy").map((c) => c.name).join(", ")}.`}
          </AlertDescription>
        </Alert>
      )}

      <section aria-labelledby="health-heading" className="space-y-3">
        <h2 id="health-heading" className="text-sm font-semibold">
          Health
        </h2>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {health.components.map((component) => (
            <Card key={component.name}>
              <CardHeader>
                <CardTitle className="flex items-center justify-between gap-2 text-sm">
                  {component.name}
                  <StatusBadge status={component.reachable ? component.status : "Unreachable"} tone={HEALTH_TONES[component.status]} />
                </CardTitle>
              </CardHeader>
              <CardContent>
                {component.checks.length === 0 ? (
                  <p className="text-xs text-muted-foreground">{component.reachable ? "No checks reported." : "No answer within 5 s."}</p>
                ) : (
                  // Check names differ per service: listed as reported.
                  <ul className="space-y-0.5 text-xs">
                    {component.checks.map((check) => (
                      <li key={check.name} className="flex justify-between gap-2">
                        <span className="font-mono">{check.name}</span>
                        <span className={check.status === "Healthy" ? "text-muted-foreground" : "text-destructive"}>{check.status}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </CardContent>
            </Card>
          ))}
        </div>
      </section>

      <div className="grid gap-6 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>Settings</CardTitle>
          </CardHeader>
          <CardContent>
            <dl>
              <Row label="Currency">{settings.pricing?.currency ?? "—"}</Row>
              <Row label="Tax">{settings.pricing ? (settings.pricing.taxApplied ? "Applied" : "None") : "—"}</Row>
              <Row label="Shipping">{settings.pricing ? (settings.pricing.shippingCharged ? "Charged" : "Free") : "—"}</Row>
              <Row label="Payments">
                {settings.payments ? (settings.payments.provider === "Stripe" ? "Card (Stripe)" : "Simulator (Stripe off)") : "—"}
              </Row>
            </dl>
            <Unavailable services={settings.unavailableServices} />
            <p className="mt-2 text-xs text-muted-foreground">Changing a setting is a redeploy.</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Payment simulator</CardTitle>
          </CardHeader>
          <CardContent>
            {flags.payments ? (
              <dl>
                <Row label="Settles every order">{flags.payments.simulatorActive ? "Yes (Stripe off)" : "No"}</Row>
                <Row label="Mode">{flags.payments.simulationMode}</Row>
                <Row label="Success rate">{`${flags.payments.successRatePercent} %`}</Row>
                <Row label="Delay">{`${flags.payments.processingDelayMinSeconds}–${flags.payments.processingDelayMaxSeconds} s`}</Row>
                <Row label="Webhook signatures">{flags.payments.webhookSignatureVerificationSkipped ? "Not checked" : "Checked"}</Row>
              </dl>
            ) : (
              <Unavailable services={flags.unavailableServices} />
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Catalog cache</CardTitle>
          </CardHeader>
          <CardContent>
            <CacheForm action={invalidateCacheAction} />
          </CardContent>
        </Card>
      </div>

      <section aria-labelledby="simulation-heading" className="space-y-3">
        <h2 id="simulation-heading" className="text-sm font-semibold">
          {`Gateway fault injection: ${flags.gatewaySimulation.enabled ? "ON" : "off"}`}
        </h2>
        <p className="text-xs text-muted-foreground">
          {`A route injects failures and delays only when it and the master switch are on.${flags.gatewaySimulation.allowHeaderOverride ? " A request header may override it." : ""}`}
        </p>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Route</TableHead>
              <TableHead>Path</TableHead>
              <TableHead>Active</TableHead>
              <TableHead className="text-right">Error rate</TableHead>
              <TableHead className="text-right">Delay</TableHead>
              <TableHead>Forced failure</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {flags.gatewaySimulation.routes.map((route) => (
              <TableRow key={route.routeId}>
                <TableCell className="font-mono text-xs">{route.routeId}</TableCell>
                <TableCell className="font-mono text-xs">{route.pathPrefix}</TableCell>
                <TableCell>{route.active ? "Yes" : route.enabled ? "Route on, master off" : "No"}</TableCell>
                <TableCell className="text-right tabular-nums">{`${Math.round(route.errorRate * 100)} %`}</TableCell>
                <TableCell className="text-right tabular-nums">{`${route.delayMinMs}–${route.delayMaxMs} ms`}</TableCell>
                <TableCell className="text-muted-foreground">{route.forcedFailureMode ?? "—"}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </section>
    </div>
  );
}
