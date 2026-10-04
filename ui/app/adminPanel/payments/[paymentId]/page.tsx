import Link from "next/link";
import { notFound } from "next/navigation";
import AccessDenied from "@/components/Admin/accessDenied";
import ActionButton from "@/components/Admin/actionButton";
import PageHeader from "@/components/Admin/pageHeader";
import StatusBadge, { PAYMENT_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission, type AdminSession } from "@/lib/admin/auth";
import { formatDateTime, formatMoney } from "@/lib/admin/format";
import { getPayment, getPaymentEvents } from "@/lib/admin/payment";
import type { Payment, PaymentEvent } from "@/lib/admin/types/payment";
import { refundAction, settleOfflineAction, settleSimulatorAction } from "../actions";
import { methodLabel, PAYMENTS_PATH } from "../filters";
import { OfflineSettleForm, RefundForm } from "../paymentForms";

export const metadata = { title: "Payment · Admin · EShop" };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const NOTICES: Record<string, string> = {
  offline: "Marked as paid offline. The order becomes Paid within seconds (refresh to see it).",
  simulated: "The simulator settled it. The order becomes Paid within seconds.",
  simulatedFailed: "The simulator declined it: the payment is Failed and the order is being cancelled.",
  refunded: "Refunded in full. The order becomes Refunded within seconds and the customer is emailed.",
};

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}

/** "system" for a message, null for a Stripe delivery (anonymous), otherwise a user id. */
function Actor({ actorId, session }: { actorId: string | null; session: AdminSession }) {
  if (actorId === null) return <>Stripe</>;
  if (actorId === "system") return <>System</>;
  if (actorId === session.id) return <>You</>;
  return hasPermission(session, "users.read") ? (
    <Link href={`/adminPanel/users/${actorId}`} className="font-mono hover:underline">
      {actorId.slice(0, 8)}
    </Link>
  ) : (
    <span className="font-mono">{actorId.slice(0, 8)}</span>
  );
}

function Timeline({ events, session }: { events: PaymentEvent[]; session: AdminSession }) {
  return (
    <ol className="space-y-3">
      {events.map((event) => (
        <li key={event.id} className="border-l-2 pl-3 text-sm">
          <p>
            {event.fromStatus && event.fromStatus !== event.toStatus ? `${event.fromStatus} → ` : ""}
            <span className="font-medium">{event.toStatus}</span>
            {event.kind === "Webhook" && <span className="text-muted-foreground"> · Stripe webhook</span>}
          </p>
          <p className="text-muted-foreground [overflow-wrap:anywhere]">{event.detail}</p>
          <p className="text-xs text-muted-foreground">
            <Actor actorId={event.actorId} session={session} /> · {formatDateTime(event.occurredAt)}
            {event.stripeEventId && <span className="font-mono">{` · ${event.stripeEventId}`}</span>}
          </p>
        </li>
      ))}
    </ol>
  );
}

export default async function PaymentPage({ params, searchParams }: PageProps<"/adminPanel/payments/[paymentId]">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "payments.read")) return <AccessDenied />;

  const { paymentId } = await params;
  if (!GUID.test(paymentId) || /^0{8}-/.test(paymentId)) notFound();
  const { done } = await searchParams;

  const [paymentResult, eventsResult] = await Promise.allSettled([getPayment(paymentId), getPaymentEvents(paymentId)]);
  if (paymentResult.status === "rejected") {
    const failure = classifyFailure(paymentResult.reason);
    if (failure?.kind === "notFound") notFound();
    if (failure?.kind === "forbidden") return <AccessDenied />;
    throw paymentResult.reason;
  }
  const payment: Payment = paymentResult.value;
  if (eventsResult.status === "rejected" && !classifyFailure(eventsResult.reason)) throw eventsResult.reason;

  const canWrite = hasPermission(session, "payments.write");
  const canRefund = hasPermission(session, "payments.refund");
  const pending = payment.status === "Pending";
  const offline = payment.paymentIntentId?.startsWith("offline:") ?? false;
  const notice = typeof done === "string" ? NOTICES[done] : undefined;

  return (
    <div className="space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href={PAYMENTS_PATH} />}>Payments</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage className="font-mono">{payment.id.slice(0, 8)}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader title={`Payment ${payment.id.slice(0, 8)}`} description={`${formatMoney(payment.amount)} ${payment.currency}`}>
        <StatusBadge status={payment.status} tone={PAYMENT_STATUS_TONES[payment.status]} />
        {hasPermission(session, "audit.read") && (
          <Link href={`/adminPanel/audit?entityId=${payment.id}`} className="text-sm underline">
            Audit trail
          </Link>
        )}
      </PageHeader>

      {notice && (
        <Alert role="status">
          <AlertDescription>{notice}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="min-w-0 space-y-6 lg:col-span-2">
          <Card>
            <CardHeader>
              <CardTitle>Details</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-4 sm:grid-cols-3">
                <Field label="Order">
                  <Link href={`/adminPanel/orders/${payment.orderId}`} className="font-mono text-xs hover:underline">
                    {payment.orderId}
                  </Link>
                </Field>
                <Field label="Customer">
                  {hasPermission(session, "users.read") ? (
                    <Link href={`/adminPanel/users/${payment.userId}`} className="font-mono text-xs hover:underline">
                      {payment.userId}
                    </Link>
                  ) : (
                    <span className="font-mono text-xs">{payment.userId}</span>
                  )}
                </Field>
                <Field label="Method">{methodLabel(payment.paymentMethod, payment.paymentIntentId)}</Field>
                <Field label="Intent / reference">
                  <span className="font-mono text-xs">{payment.paymentIntentId ?? "—"}</span>
                </Field>
                <Field label="Created">{formatDateTime(payment.createdAt)}</Field>
                <Field label="Processed">{formatDateTime(payment.processedAt)}</Field>
                {payment.errorMessage && (
                  <div className="sm:col-span-3">
                    <Field label={payment.status === "Refunded" ? "Refund reason" : "Message"}>{payment.errorMessage}</Field>
                  </div>
                )}
              </dl>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Timeline</CardTitle>
            </CardHeader>
            <CardContent>
              {eventsResult.status === "fulfilled" ? (
                <Timeline events={eventsResult.value} session={session} />
              ) : (
                <p className="text-sm text-muted-foreground">The timeline could not be loaded.</p>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="min-w-0 space-y-6">
          {pending && canWrite && (
            <>
              <Card>
                <CardHeader>
                  <CardTitle>Received outside the shop</CardTitle>
                </CardHeader>
                <CardContent>
                  <OfflineSettleForm action={settleOfflineAction.bind(null, payment.id, payment.orderId)} />
                </CardContent>
              </Card>
              <Card>
                <CardHeader>
                  <CardTitle>Simulator</CardTitle>
                </CardHeader>
                <CardContent className="space-y-3">
                  <p className="text-sm text-muted-foreground">
                    A testing tool: the simulator decides the outcome after a short delay. A declined settle fails the
                    payment and cancels the order.
                  </p>
                  <ActionButton
                    action={settleSimulatorAction.bind(null, payment.id, payment.orderId)}
                    align="start"
                    confirm={{
                      title: "Settle through the simulator?",
                      description: "The simulator may decline it, which fails the payment and cancels the order.",
                      confirmLabel: "Settle",
                    }}
                  >
                    Settle with the simulator
                  </ActionButton>
                </CardContent>
              </Card>
            </>
          )}

          {payment.status === "Success" && canRefund && (
            <Card>
              <CardHeader>
                <CardTitle>Refund</CardTitle>
              </CardHeader>
              <CardContent>
                <RefundForm
                  action={refundAction.bind(null, payment.id, payment.orderId)}
                  amountLabel={formatMoney(payment.amount)}
                  offline={payment.paymentMethod === "Mock" && offline}
                />
              </CardContent>
            </Card>
          )}

          {payment.status === "Processing" && (
            <p className="text-sm text-muted-foreground">
              The customer has a card payment open at Stripe; it settles when Stripe confirms it.
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
