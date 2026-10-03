import Link from "next/link";
import { ChevronRight, Inbox } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button, buttonVariants } from "@/components/ui/button";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { listAbandonedBaskets, listBaskets } from "@/lib/admin/basket";
import { formatDateTime, formatMoney } from "@/lib/admin/format";
import { buildAdminHref } from "@/lib/admin/href";
import type { AdminBasketPageDto } from "@/lib/admin/types/basket";
import { cn } from "@/lib/utils";

export const metadata = { title: "Baskets · Admin · EShop" };

const BASKETS_PATH = "/adminPanel/baskets";
const PAGE_SIZE = 20;
/** The API takes "<n>m|h|d", at most 30 days. */
const AGES = { "1h": "1 hour", "24h": "24 hours", "3d": "3 days", "7d": "7 days", "30d": "30 days" } as const;
type Age = keyof typeof AGES;

function first(value: string | string[] | undefined): string {
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

export default async function BasketsPage({ searchParams }: PageProps<"/adminPanel/baskets">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "baskets.read")) return <AccessDenied />;

  const params = await searchParams;
  const view = first(params.view) === "abandoned" ? "abandoned" : "all";
  const olderThan: Age = first(params.olderThan) in AGES ? (first(params.olderThan) as Age) : "24h";
  const cursor = first(params.cursor) || undefined;

  let page: AdminBasketPageDto | null = null;
  let problem: string | null = null;
  try {
    page =
      view === "abandoned"
        ? await listAbandonedBaskets({ olderThan, cursor, pageSize: PAGE_SIZE })
        : await listBaskets({ cursor, pageSize: PAGE_SIZE });
  } catch (error) {
    const failure = classifyFailure(error);
    if (failure?.kind === "forbidden") return <AccessDenied />;
    if (failure?.kind !== "invalid" && failure?.kind !== "rateLimited") throw error;
    // A stale or foreign cursor is a 400: offer the first page again.
    problem = failure.message;
  }
  const base = { view: view === "all" ? undefined : view, olderThan: view === "abandoned" ? olderThan : undefined };

  return (
    <div className="space-y-6">
      <PageHeader title="Baskets" description="Stored customer baskets, read-only.">
        {hasPermission(session, "system.manage") && (
          <Link href={`${BASKETS_PATH}/outbox`} className={cn(buttonVariants({ variant: "outline" }))}>
            <Inbox aria-hidden data-icon="inline-start" />
            Checkout outbox
          </Link>
        )}
      </PageHeader>

      <form action={BASKETS_PATH} aria-label="Choose baskets" className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <label htmlFor="view" className="text-xs font-medium text-muted-foreground">
            Show
          </label>
          <NativeSelect id="view" name="view" defaultValue={view}>
            <NativeSelectOption value="all">Every basket</NativeSelectOption>
            <NativeSelectOption value="abandoned">Abandoned only</NativeSelectOption>
          </NativeSelect>
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="olderThan" className="text-xs font-medium text-muted-foreground">
            Abandoned: unchanged for
          </label>
          <NativeSelect id="olderThan" name="olderThan" defaultValue={olderThan}>
            {Object.entries(AGES).map(([key, label]) => (
              <NativeSelectOption key={key} value={key}>
                {label}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <Button type="submit">Show</Button>
      </form>

      {view === "abandoned" && page?.modifiedBefore && (
        <p className="text-sm text-muted-foreground">{`Unchanged since before ${formatDateTime(page.modifiedBefore)}.`}</p>
      )}

      {problem && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>
            {problem}{" "}
            <Link href={buildAdminHref(BASKETS_PATH, base)} className="underline">
              Start from the first page
            </Link>
          </AlertDescription>
        </Alert>
      )}

      {page && (
        <>
          {page.items.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Customer</TableHead>
                  <TableHead className="text-right">Lines</TableHead>
                  <TableHead className="text-right">Items</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                  <TableHead>Last changed (UTC)</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {page.items.map((basket) => (
                  <TableRow key={basket.userId}>
                    <TableCell>
                      <Link href={`${BASKETS_PATH}/${basket.userId}`} className="font-mono text-xs hover:underline">
                        {basket.userId}
                      </Link>
                      {!basket.isReadable && (
                        <Badge variant="destructive" className="ml-2">
                          Unreadable
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{basket.lines ?? "—"}</TableCell>
                    <TableCell className="text-right tabular-nums">{basket.totalItems ?? "—"}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {basket.totalPrice === null ? "—" : formatMoney(basket.totalPrice)}
                    </TableCell>
                    <TableCell className="text-muted-foreground tabular-nums">{formatDateTime(basket.lastModifiedAt)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <p className="text-center text-sm text-muted-foreground">
              {page.nextCursor
                ? "Nothing on this page: the scan walks the store in chunks, so a page can be empty while more remain."
                : cursor
                  ? "No more baskets."
                  : view === "abandoned"
                    ? "No basket has been left that long."
                    : "No stored baskets."}
            </p>
          )}
          <nav aria-label="Pagination" className="flex items-center justify-between gap-4">
            {cursor ? (
              <Link href={buildAdminHref(BASKETS_PATH, base)} className="text-sm underline">
                First page
              </Link>
            ) : (
              <span />
            )}
            {page.nextCursor && (
              <Link
                href={buildAdminHref(BASKETS_PATH, { ...base, cursor: page.nextCursor })}
                className={cn(buttonVariants({ variant: "outline", size: "sm" }))}
              >
                Next page
                <ChevronRight aria-hidden data-icon="inline-end" />
              </Link>
            )}
          </nav>
        </>
      )}
    </div>
  );
}
