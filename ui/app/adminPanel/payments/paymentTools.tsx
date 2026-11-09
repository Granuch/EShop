import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { classifyFailure } from "@/lib/admin/api";
import { getSimulation } from "@/lib/admin/payment";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { PaymentSimulationDiagnostics } from "@/lib/admin/types/payment";
import ReplayButton from "./replayButton";

/** The operator tools under the list: webhook replay (payments.write) and the simulator's settings (Admin role). */
async function PaymentTools({ canWrite }: { canWrite: boolean }) {
  let simulation: PaymentSimulationDiagnostics | null = null;
  let simulationProblem: string | null = null;
  try {
    simulation = await getSimulation();
  } catch (error) {
    const failure = classifyFailure(error);
    if (!failure) throw error;
    simulationProblem = failure.kind === "forbidden" ? ADMIN_ROLE_HINTS.payment : "The settings could not be loaded.";
  }

  return (
    <section aria-label="Payment tools" className="grid gap-6 md:grid-cols-2">
      {canWrite && (
        <Card>
          <CardHeader>
            <CardTitle>Failed Stripe webhooks</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <p className="text-sm text-muted-foreground">
              Deliveries Stripe sent that Payment accepted but could not apply (for example while its database was
              down) are kept and can be applied again. Nothing is changed twice.
            </p>
            <ReplayButton />
          </CardContent>
        </Card>
      )}
      <Card>
        <CardHeader>
          <CardTitle>Payment simulator</CardTitle>
        </CardHeader>
        <CardContent>
          {simulation ? (
            <dl className="grid grid-cols-2 gap-3 text-sm">
              <div>
                <dt className="text-xs text-muted-foreground">Mode</dt>
                <dd>{simulation.mode}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">Success rate</dt>
                <dd>{`${simulation.successRatePercent} %`}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">Processing delay</dt>
                <dd>{`${simulation.processingDelayMinSeconds}–${simulation.processingDelayMaxSeconds} s`}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">Refund delay</dt>
                <dd>{`${simulation.refundDelaySeconds} s`}</dd>
              </div>
              <div className="col-span-2">
                <dt className="text-xs text-muted-foreground">Forced failure reason</dt>
                <dd>{simulation.forcedFailureReason}</dd>
              </div>
              <p className="col-span-2 text-xs text-muted-foreground">
                Used by simulator settles, by refunds of offline and simulator payments, and for every order when
                Stripe is off. Read-only here.
              </p>
            </dl>
          ) : (
            <p className="text-sm text-muted-foreground">{simulationProblem}</p>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

export default PaymentTools;
