import Link from "next/link";
import { FileText } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import Pager from "@/components/Admin/pager";
import StatusBadge, { NOTIFICATION_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { formatDateTime } from "@/lib/admin/format";
import { buildAdminHref } from "@/lib/admin/href";
import { getNotificationStats, listNotifications } from "@/lib/admin/notification";
import type { PagedResult } from "@/lib/admin/types/common";
import type { NotificationStats, NotificationSummary } from "@/lib/admin/types/notification";
import { cn } from "@/lib/utils";
import { retryFailedAction } from "./actions";
import {
  hasActiveFilters,
  NOTIFICATIONS_PATH,
  parseNotificationFilters,
  toLinkParams,
  toNotificationFilterQuery,
  toNotificationListQuery,
} from "./filters";
import NotificationFilters from "./notificationFilters";
import { RetryFailedForm } from "./notificationForms";

export const metadata = { title: "Notifications · Admin · EShop" };

function StatsStrip({ stats }: { stats: NotificationStats }) {
  return (
    <dl className="grid grid-cols-2 gap-4 rounded-lg border p-4 sm:grid-cols-5" aria-label="Notification statistics">
      {[
        ["Total", stats.total],
        ["Sent", stats.sent],
        ["Failed", stats.failed],
        ["Queued", stats.queued],
        ["Undeliverable", stats.undeliverable],
      ].map(([label, value]) => (
        <div key={label}>
          <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
          <dd className="text-lg font-semibold tabular-nums">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

export default async function NotificationsPage({ searchParams }: PageProps<"/adminPanel/notifications">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "notifications.read")) return <AccessDenied />;

  const filters = parseNotificationFilters(await searchParams);
  const [listResult, statsResult] = await Promise.allSettled([
    listNotifications(toNotificationListQuery(filters)),
    getNotificationStats(toNotificationFilterQuery(filters)),
  ]);

  let page: PagedResult<NotificationSummary> | null = null;
  let problem: string | null = null;
  if (listResult.status === "fulfilled") {
    page = listResult.value;
  } else {
    const failure = classifyFailure(listResult.reason);
    if (failure?.kind === "forbidden") return <AccessDenied />;
    if (failure?.kind !== "invalid" && failure?.kind !== "rateLimited") throw listResult.reason;
    problem = failure.message;
  }
  const linkParams = toLinkParams(filters);
  const canManage = hasPermission(session, "notifications.manage");

  return (
    <div className="space-y-6">
      <PageHeader title="Notifications" description="Every customer email and its delivery, newest first. Kept 90 days.">
        <Link href={`${NOTIFICATIONS_PATH}/templates`} className={cn(buttonVariants({ variant: "outline" }))}>
          <FileText aria-hidden data-icon="inline-start" />
          Templates
        </Link>
      </PageHeader>

      <NotificationFilters filters={filters} />

      {statsResult.status === "fulfilled" && <StatsStrip stats={statsResult.value} />}

      {/* Always mounted for managers: hiding it when nothing has failed would drop the result of the retry that just
          cleared the failures (its message lives in the form's state). */}
      {canManage && (
        <section aria-label="Retry failed notifications" className="space-y-2 rounded-lg border p-4">
          <h2 className="text-sm font-semibold">
            {statsResult.status === "fulfilled" ? `Retry failed (${statsResult.value.failed} matching now)` : "Retry failed"}
          </h2>
          <p className="text-xs text-muted-foreground">
            Sends the Failed notifications that match the template, email, user and dates above again, oldest first
            (the status filter does not apply: only Failed ones are retried). Password resets are never kept, so never
            retried.
          </p>
          <RetryFailedForm
            action={retryFailedAction}
            filters={{
              templateName: filters.templateName,
              email: filters.email,
              userId: filters.userId,
              from: filters.from,
              to: filters.to,
            }}
          />
        </section>
      )}

      {problem && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{problem}</AlertDescription>
        </Alert>
      )}

      {page && page.items.length > 0 && (
        <>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Subject</TableHead>
                <TableHead>Template</TableHead>
                <TableHead>Recipient</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Failures</TableHead>
                <TableHead>Created (UTC)</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((row) => (
                <TableRow key={row.id}>
                  <TableCell>
                    <Link href={`${NOTIFICATIONS_PATH}/${row.id}`} className="block max-w-72 truncate hover:underline" title={row.subject}>
                      {row.subject}
                    </Link>
                  </TableCell>
                  <TableCell className="font-mono text-xs text-muted-foreground">{row.templateName}</TableCell>
                  <TableCell className="text-muted-foreground">{row.recipientEmail ?? "—"}</TableCell>
                  <TableCell>
                    <StatusBadge status={row.status} tone={NOTIFICATION_STATUS_TONES[row.status]} />
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{row.retryCount}</TableCell>
                  <TableCell className="text-muted-foreground tabular-nums">{formatDateTime(row.createdAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pager
            path={NOTIFICATIONS_PATH}
            params={linkParams}
            pageNumber={page.pageNumber}
            totalPages={page.totalPages}
            totalCount={page.totalCount}
          />
        </>
      )}

      {page && page.items.length === 0 && (
        <div className="mt-12 text-center">
          <p className="font-medium">
            {page.totalCount > 0 ? "No results on this page" : hasActiveFilters(filters) ? "No notifications match these filters" : "No notifications yet"}
          </p>
          <Link href={page.totalCount > 0 ? buildAdminHref(NOTIFICATIONS_PATH, linkParams) : NOTIFICATIONS_PATH} className="mt-2 inline-block text-sm underline">
            {page.totalCount > 0 ? "Go to page 1" : "Clear filters"}
          </Link>
        </div>
      )}
    </div>
  );
}
