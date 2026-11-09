import Link from "next/link";
import { notFound } from "next/navigation";
import AccessDenied from "@/components/Admin/accessDenied";
import ActionButton from "@/components/Admin/actionButton";
import PageHeader from "@/components/Admin/pageHeader";
import StatusBadge, { NOTIFICATION_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { formatDateTime } from "@/lib/admin/format";
import { getNotification } from "@/lib/admin/notification";
import type { NotificationDetail } from "@/lib/admin/types/notification";
import { cn } from "@/lib/utils";
import { markUndeliverableAction, resendAction } from "../actions";
import { NOTIFICATIONS_PATH } from "../filters";
import { MarkUndeliverableForm } from "../notificationForms";

export const metadata = { title: "Notification · Admin · EShop" };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** An attempt holds the row this long; after it, a Sending row may be resent or closed (notification.md "Status"). */
const LEASE_MS = 5 * 60_000;

const NOTICES: Record<string, string> = {
  resent: "Handed to delivery again. It shows Sent (or Failed) in a few seconds: refresh.",
  undeliverable: "Marked undeliverable. Nothing will be sent for it again.",
};

/** A Sending row whose attempt started less than the lease ago: the API refuses resend and close until it ends. */
function attemptInProgress(row: NotificationDetail): boolean {
  return row.status === "Sending" && row.attemptStartedAt !== null && Date.now() - new Date(row.attemptStartedAt).getTime() < LEASE_MS;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}

export default async function NotificationPage({ params, searchParams }: PageProps<"/adminPanel/notifications/[notificationId]">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "notifications.read")) return <AccessDenied />;

  const { notificationId } = await params;
  if (!GUID.test(notificationId) || /^0{8}-/.test(notificationId)) notFound();
  const { done } = await searchParams;

  let row: NotificationDetail;
  try {
    row = await getNotification(notificationId);
  } catch (error) {
    const failure = classifyFailure(error);
    if (failure?.kind === "notFound") notFound();
    if (failure?.kind === "forbidden") return <AccessDenied />;
    throw error;
  }

  const canManage = hasPermission(session, "notifications.manage");
  const leaseHeld = attemptInProgress(row);
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
            <BreadcrumbLink render={<Link href={NOTIFICATIONS_PATH} />}>Notifications</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage className="font-mono">{row.id.slice(0, 8)}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader title={row.subject} description={`${row.templateName} · ${row.eventType}`}>
        <StatusBadge status={row.status} tone={NOTIFICATION_STATUS_TONES[row.status]} />
        <Link href={`${NOTIFICATIONS_PATH}/${row.id}`} className={cn(buttonVariants({ variant: "outline", size: "sm" }))} prefetch={false}>
          Refresh
        </Link>
      </PageHeader>

      {notice && (
        <Alert role="status">
          <AlertDescription>{notice}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="min-w-0 lg:col-span-2">
          <CardHeader>
            <CardTitle>Delivery</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-4 sm:grid-cols-2">
              <Field label="Recipient">{row.recipientEmail ?? "Not known yet"}</Field>
              <Field label="User">
                {row.userId && hasPermission(session, "users.read") ? (
                  <Link href={`/adminPanel/users/${row.userId}`} className="font-mono text-xs hover:underline">
                    {row.userId}
                  </Link>
                ) : (
                  <span className="font-mono text-xs">{row.userId ?? "—"}</span>
                )}
              </Field>
              <Field label="Failed attempts">{row.retryCount}</Field>
              <Field label="Last error">{row.lastError ?? "None"}</Field>
              <Field label="Created">{formatDateTime(row.createdAt)}</Field>
              <Field label="Sent">{formatDateTime(row.sentAt)}</Field>
              <Field label="Last attempt started">{formatDateTime(row.attemptStartedAt)}</Field>
              <Field label="Updated">{formatDateTime(row.updatedAt)}</Field>
              <Field label="Mail server Message-ID">
                <span className="font-mono text-xs">{row.providerMessageId ?? "—"}</span>
              </Field>
              <Field label="Correlation id (search the logs with it)">
                <span className="font-mono text-xs">{row.correlationId ?? "—"}</span>
              </Field>
              <Field label="Event id">
                <span className="font-mono text-xs">{row.eventId}</span>
              </Field>
            </dl>
          </CardContent>
        </Card>

        <div className="min-w-0 space-y-6">
          {row.isFinal ? (
            <p className="text-sm text-muted-foreground">
              {row.status === "Sent" ? "Delivered to the mail server." : "Closed as undeliverable."} A final notification is
              never attempted again.
            </p>
          ) : (
            canManage && (
              <>
                {leaseHeld && (
                  <Alert>
                    <AlertDescription>
                      An attempt is in progress. Resend and close are refused until it ends (at most 5 minutes after it
                      started).
                    </AlertDescription>
                  </Alert>
                )}
                <Card>
                  <CardHeader>
                    <CardTitle>Send again</CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-3">
                    {row.isResendable ? (
                      <ActionButton action={resendAction.bind(null, row.id)} align="start" disabled={leaseHeld}>
                        Resend
                      </ActionButton>
                    ) : (
                      <p className="text-sm text-muted-foreground">
                        Its event was not kept (a password reset or a confirmation link carries a live token), so it cannot
                        be sent again. Ask the customer to request a new one.
                      </p>
                    )}
                  </CardContent>
                </Card>
                <Card>
                  <CardHeader>
                    <CardTitle>Close</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <MarkUndeliverableForm action={markUndeliverableAction.bind(null, row.id)} />
                  </CardContent>
                </Card>
              </>
            )
          )}
        </div>
      </div>
    </div>
  );
}
