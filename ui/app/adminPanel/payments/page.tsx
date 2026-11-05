import Link from "next/link";
import { Download } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import Pager from "@/components/Admin/pager";
import StatusBadge, { PAYMENT_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { endOfDayUtc, formatDateTime, formatMoney, startOfDayUtc } from "@/lib/admin/format";
import { buildAdminHref } from "@/lib/admin/href";
import { getPaymentStats, listPayments } from "@/lib/admin/payment";
import type { PagedResult } from "@/lib/admin/types/common";
import type { Payment, PaymentStats } from "@/lib/admin/types/payment";
import { cn } from "@/lib/utils";
import {
  hasActiveFilters,
  methodLabel,
  parsePaymentFilters,
  PAYMENTS_PATH,
  toLinkParams,
  toPaymentListQuery,
} from "./filters";
import PaymentFilters from "./paymentFilters";
import PaymentTools from "./paymentTools";

export const metadata = { title: "Payments · Admin · EShop" };

const DAYS = 30;

function statsWindow(now = new Date()) {
  const day = (offset: number) => new Date(now.getTime() - offset * 86_400_000).toISOString().slice(0, 10);
  return { from: startOfDayUtc(day(DAYS - 1))!, to: endOfDayUtc(day(0))! };
}

function StatsStrip({ stats }: { stats: PaymentStats }) {
  return (
    <section aria-label={`Payments, last ${DAYS} days`} className="space-y-3 rounded-lg border p-4">
      <h2 className="text-sm font-semibold">{`Last ${DAYS} days`}</h2>
      <dl className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {[
          ["Payments", String(stats.totalPayments)],
          ["Captured revenue", formatMoney(stats.capturedRevenue)],
          ["Refunded", formatMoney(stats.refundedAmount)],
          ["Failed", formatMoney(stats.failedAmount)],
        ].map(([label, value]) => (
          <div key={label}>
            <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
            <dd className="text-lg font-semibold tabular-nums">{value}</dd>
          </div>
        ))}
      </dl>
      <ul className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
        {stats.byStatus.map((entry) => (
          <li key={entry.status}>
            <Link href={`${PAYMENTS_PATH}?status=${entry.status}`} className="hover:underline">
              {`${entry.status} ${entry.count}`}
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

export default async function PaymentsPage({ searchParams }: PageProps<"/adminPanel/payments">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "payments.read")) return <AccessDenied />;

  const params = await searchParams;
  const filters = parsePaymentFilters(params);
  const exportError = typeof params.exportError === "string" ? params.exportError : null;
  const [listResult, statsResult] = await Promise.allSettled([
    listPayments(toPaymentListQuery(filters)),
    getPaymentStats({ ...statsWindow(), groupBy: "Day" }),
  ]);

  let page: PagedResult<Payment> | null = null;
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

  return (
    <div className="space-y-6">
      <PageHeader
        title="Payments"
        description={page ? `${page.totalCount} ${page.totalCount === 1 ? "payment" : "payments"}, newest first` : undefined}
      >
        {/* A plain <a>: the export is a route handler answering a file. */}
        <a href={buildAdminHref(`${PAYMENTS_PATH}/export`, linkParams)} className={cn(buttonVariants({ variant: "outline" }))}>
          <Download aria-hidden data-icon="inline-start" />
          Export CSV
        </a>
      </PageHeader>

      {statsResult.status === "fulfilled" && <StatsStrip stats={statsResult.value} />}

      <PaymentFilters filters={filters} />

      {(problem || exportError) && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{problem ?? exportError}</AlertDescription>
        </Alert>
      )}

      {page && page.items.length > 0 && (
        <>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Payment</TableHead>
                <TableHead>Order</TableHead>
                <TableHead>Method</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Amount</TableHead>
                <TableHead>Created (UTC)</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((payment) => (
                <TableRow key={payment.id}>
                  <TableCell>
                    <Link href={`${PAYMENTS_PATH}/${payment.id}`} className="font-mono text-xs hover:underline">
                      {payment.id.slice(0, 8)}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Link href={`/adminPanel/orders/${payment.orderId}`} className="font-mono text-xs hover:underline">
                      {payment.orderId.slice(0, 8)}
                    </Link>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{methodLabel(payment.paymentMethod, payment.paymentIntentId)}</TableCell>
                  <TableCell>
                    <StatusBadge status={payment.status} tone={PAYMENT_STATUS_TONES[payment.status]} />
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatMoney(payment.amount)}</TableCell>
                  <TableCell className="text-muted-foreground tabular-nums">{formatDateTime(payment.createdAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pager
            path={PAYMENTS_PATH}
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
            {page.totalCount > 0 ? "No results on this page" : hasActiveFilters(filters) ? "No payments match these filters" : "No payments yet"}
          </p>
          <Link href={page.totalCount > 0 ? buildAdminHref(PAYMENTS_PATH, linkParams) : PAYMENTS_PATH} className="mt-2 inline-block text-sm underline">
            {page.totalCount > 0 ? "Go to page 1" : "Clear filters"}
          </Link>
        </div>
      )}

      <PaymentTools canWrite={hasPermission(session, "payments.write")} />
    </div>
  );
}
