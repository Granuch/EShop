import Link from "next/link";
import { ChevronRight } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import StatusBadge from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button, buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { formatDateTime, startOfDayUtc } from "@/lib/admin/format";
import { buildAdminHref } from "@/lib/admin/href";
import { getAuditLog } from "@/lib/admin/platform";
import type { AuditLogEntry, AuditLogPage, AuditOutcome, AuditService } from "@/lib/admin/types/platform";
import { cn } from "@/lib/utils";

export const metadata = { title: "Audit trail · Admin · EShop" };

const AUDIT_PATH = "/adminPanel/audit";
const PAGE_SIZE = 50;
const SERVICES: AuditService[] = ["catalog", "identity", "notification", "ordering", "payment"];
const OUTCOMES: AuditOutcome[] = ["Succeeded", "Rejected", "Failed"];
const OUTCOME_TONES = { Succeeded: "success", Rejected: "warning", Failed: "danger" } as const;
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const TEXT_FILTERS = ["action", "entityType", "entityId", "actorUserId"] as const;

/** Where an audited entity lives in this admin, when it has a page. */
const ENTITY_PAGES: Record<string, string> = {
  Product: "/adminPanel/products/",
  Category: "/adminPanel/categories/",
  Order: "/adminPanel/orders/",
  Payment: "/adminPanel/payments/",
  User: "/adminPanel/users/",
  Notification: "/adminPanel/notifications/",
};

function first(value: string | string[] | undefined): string {
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

/** The day after a YYYY-MM-DD, at 00:00 UTC: the API's `to` is exclusive, the form's is inclusive. */
function dayAfter(date: string): string {
  return new Date(Date.parse(`${date}T00:00:00Z`) + 86_400_000).toISOString();
}

/** payloadJson is a JSON string (masked, lists cut at 25; F-58): shown as formatted text, never relied on. */
function Payload({ json }: { json: string | null }) {
  if (!json) return null;
  let text = json;
  try {
    text = JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    // Shown as stored.
  }
  return (
    <details>
      <summary className="cursor-pointer text-xs underline">Input</summary>
      <pre className="mt-1 max-h-64 max-w-xl overflow-auto rounded bg-muted p-2 text-xs whitespace-pre-wrap [overflow-wrap:anywhere]">
        {text}
      </pre>
    </details>
  );
}

function Entity({ row }: { row: AuditLogEntry }) {
  if (!row.entityId) return <span className="text-muted-foreground">{row.entityType}</span>;
  const page = ENTITY_PAGES[row.entityType];
  const id = <span className="font-mono text-xs">{row.entityId.length > 12 ? row.entityId.slice(0, 8) : row.entityId}</span>;
  return (
    <span>
      <span className="text-muted-foreground">{`${row.entityType} `}</span>
      {page ? (
        <Link href={`${page}${row.entityId}`} className="hover:underline" title={row.entityId}>
          {id}
        </Link>
      ) : (
        <span title={row.entityId}>{id}</span>
      )}
    </span>
  );
}

export default async function AuditPage({ searchParams }: PageProps<"/adminPanel/audit">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "audit.read")) return <AccessDenied />;

  const params = await searchParams;
  const service = (SERVICES as string[]).includes(first(params.service)) ? (first(params.service) as AuditService) : undefined;
  const outcome = (OUTCOMES as string[]).includes(first(params.outcome)) ? (first(params.outcome) as AuditOutcome) : undefined;
  const text = Object.fromEntries(TEXT_FILTERS.map((key) => [key, first(params[key])])) as Record<(typeof TEXT_FILTERS)[number], string>;
  const from = DATE.test(first(params.from)) ? first(params.from) : "";
  const to = DATE.test(first(params.to)) ? first(params.to) : "";
  const cursor = first(params.cursor) || undefined;
  // Every filter stays the same while paging; only the cursor changes (admin-platform.md "Paging").
  const filters = { service, outcome, ...text, from, to };

  let page: AuditLogPage | null = null;
  let problem: string | null = null;
  try {
    page = await getAuditLog({
      service,
      outcome,
      action: text.action || undefined,
      entityType: text.entityType || undefined,
      entityId: text.entityId || undefined,
      actorUserId: text.actorUserId || undefined,
      from: startOfDayUtc(from),
      to: to ? dayAfter(to) : undefined,
      cursor,
      pageSize: PAGE_SIZE,
    });
  } catch (error) {
    const failure = classifyFailure(error);
    if (failure?.kind === "forbidden") return <AccessDenied />;
    if (failure?.kind !== "invalid" && failure?.kind !== "rateLimited") throw error;
    problem = failure.message;
  }
  // F-57: with a service unavailable, nextCursor never ends; an empty page is the end for this view.
  const nextCursor = page && page.items.length > 0 ? page.nextCursor : null;
  const label = "text-xs font-medium text-muted-foreground";

  return (
    <div className="space-y-6">
      <PageHeader title="Audit trail" description="Every admin action in Catalog, Identity, Notification, Ordering and Payment, newest first." />

      <form action={AUDIT_PATH} role="search" aria-label="Filter the audit trail" className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <label htmlFor="service" className={label}>
            Service
          </label>
          <NativeSelect id="service" name="service" defaultValue={service ?? ""}>
            <NativeSelectOption value="">All five</NativeSelectOption>
            {SERVICES.map((s) => (
              <NativeSelectOption key={s} value={s}>
                {s}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="outcome" className={label}>
            Outcome
          </label>
          <NativeSelect id="outcome" name="outcome" defaultValue={outcome ?? ""}>
            <NativeSelectOption value="">Any</NativeSelectOption>
            {OUTCOMES.map((o) => (
              <NativeSelectOption key={o} value={o}>
                {o}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <div className="flex w-44 flex-col gap-1">
          <label htmlFor="action" className={label}>
            Action (exact, e.g. RefundPayment)
          </label>
          <Input id="action" name="action" maxLength={100} defaultValue={text.action} />
        </div>
        <div className="flex w-36 flex-col gap-1">
          <label htmlFor="entityType" className={label}>
            Entity type (exact)
          </label>
          <Input id="entityType" name="entityType" maxLength={100} defaultValue={text.entityType} placeholder="Product" />
        </div>
        <div className="flex w-full flex-col gap-1 sm:w-80">
          <label htmlFor="entityId" className={label}>
            Entity id (exact)
          </label>
          <Input id="entityId" name="entityId" maxLength={200} defaultValue={text.entityId} className="font-mono" />
        </div>
        <div className="flex w-full flex-col gap-1 sm:w-80">
          <label htmlFor="actorUserId" className={label}>
            Actor user id (exact)
          </label>
          <Input id="actorUserId" name="actorUserId" maxLength={200} defaultValue={text.actorUserId} className="font-mono" />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="from" className={label}>
            From (UTC)
          </label>
          <Input id="from" name="from" type="date" defaultValue={from} />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="to" className={label}>
            To (UTC, inclusive)
          </label>
          <Input id="to" name="to" type="date" defaultValue={to} />
        </div>
        <Button type="submit">Apply</Button>
        <Link href={AUDIT_PATH} className="pb-1.5 text-sm underline">
          Clear
        </Link>
      </form>

      {problem && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{problem}</AlertDescription>
        </Alert>
      )}

      {page && page.unavailableServices.length > 0 && (
        <Alert role="status">
          <AlertDescription>
            {`Not read for this page: ${page.unavailableServices.join(", ")}. Their rows appear on a later page, out of time order. `}
            <Link href={buildAdminHref(AUDIT_PATH, { ...filters, cursor })} className="underline" prefetch={false}>
              Try again
            </Link>
          </AlertDescription>
        </Alert>
      )}

      {page && page.items.length > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>When (UTC)</TableHead>
              <TableHead>Service</TableHead>
              <TableHead>Action</TableHead>
              <TableHead>Entity</TableHead>
              <TableHead>By</TableHead>
              <TableHead>Outcome</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {page.items.map((row) => (
              // `id` is unique only within its service.
              <TableRow key={`${row.service}:${row.id}`} className="align-top">
                <TableCell className="tabular-nums">{formatDateTime(row.occurredAt)}</TableCell>
                <TableCell className="text-muted-foreground">{row.service}</TableCell>
                <TableCell className="whitespace-normal">
                  <span className="font-medium">{row.action}</span>
                  <Payload json={row.payloadJson} />
                </TableCell>
                <TableCell>
                  <Entity row={row} />
                </TableCell>
                <TableCell className="text-muted-foreground">{row.actorName ?? row.actorUserId ?? "system"}</TableCell>
                <TableCell>
                  <StatusBadge status={row.outcome} tone={OUTCOME_TONES[row.outcome]} />
                  {row.errorCode && <span className="mt-1 block font-mono text-xs text-muted-foreground">{row.errorCode}</span>}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {page && page.items.length === 0 && (
        <p className="mt-12 text-center font-medium">{cursor ? "No more entries." : "No entries match these filters."}</p>
      )}

      {page && (cursor || nextCursor) && (
        <nav aria-label="Pagination" className="flex items-center justify-between gap-4">
          {cursor ? (
            <Link href={buildAdminHref(AUDIT_PATH, filters)} className="text-sm underline">
              Newest entries
            </Link>
          ) : (
            <span />
          )}
          {nextCursor && (
            <Link
              href={buildAdminHref(AUDIT_PATH, { ...filters, cursor: nextCursor })}
              className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
            >
              Older entries
              <ChevronRight aria-hidden data-icon="inline-end" />
            </Link>
          )}
        </nav>
      )}
    </div>
  );
}
