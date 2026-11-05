import Link from "next/link";
import AccessDenied from "@/components/Admin/accessDenied";
import ActionButton from "@/components/Admin/actionButton";
import PageHeader from "@/components/Admin/pageHeader";
import Pager from "@/components/Admin/pager";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { countDeadLetters, listDeadLetters } from "@/lib/admin/basket";
import { formatDateTime } from "@/lib/admin/format";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import { replayDeadLettersAction } from "./actions";

export const metadata = { title: "Checkout outbox · Baskets · Admin · EShop" };

const OUTBOX_PATH = "/adminPanel/baskets/outbox";
const LIMIT = 20;

export default async function BasketOutboxPage({ searchParams }: PageProps<"/adminPanel/baskets/outbox">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "system.manage")) return <AccessDenied />;

  const { pageNumber: pageParam, replayed } = await searchParams;
  const pageNumber = typeof pageParam === "string" && /^\d{1,6}$/.test(pageParam) && Number(pageParam) > 0 ? Number(pageParam) : 1;

  const [detailsResult, countResult] = await Promise.allSettled([
    listDeadLetters({ offset: (pageNumber - 1) * LIMIT, limit: LIMIT }),
    countDeadLetters(),
  ]);
  if (detailsResult.status === "rejected") {
    const failure = classifyFailure(detailsResult.reason);
    if (failure?.kind === "forbidden") return <AccessDenied />;
    throw detailsResult.reason;
  }
  const page = detailsResult.value;
  // The count and the replay still need the Admin ROLE at Basket (F-09).
  const countProblem =
    countResult.status === "rejected"
      ? classifyFailure(countResult.reason)?.kind === "forbidden"
        ? ADMIN_ROLE_HINTS.basket
        : "The count could not be loaded."
      : null;

  return (
    <div className="space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel/baskets" />}>Baskets</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>Checkout outbox</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader
        title="Checkout outbox"
        description="A dead letter is a checkout whose event could not be published for hours: an order Ordering never received."
      >
        {countResult.status === "fulfilled" && countResult.value.count > 0 && (
          <ActionButton
            action={replayDeadLettersAction}
            variant="default"
            size="default"
            confirm={{
              title: `Replay ${countResult.value.count} dead ${countResult.value.count === 1 ? "letter" : "letters"}?`,
              description: "They go back on the outbox for another publish (at most 1000 per press, oldest first). Ordering then creates their orders.",
              confirmLabel: "Replay",
            }}
          >
            Replay all
          </ActionButton>
        )}
      </PageHeader>

      {typeof replayed === "string" && /^\d+$/.test(replayed) && (
        <Alert role="status">
          <AlertDescription>{`Replayed ${replayed}. They are published on the outbox's next run; Ordering creates the orders shortly after.`}</AlertDescription>
        </Alert>
      )}

      <p className="text-sm">
        {countResult.status === "fulfilled" ? `${countResult.value.count} dead ${countResult.value.count === 1 ? "letter" : "letters"} (count).` : countProblem}
      </p>

      {page.total === 0 ? (
        <Alert>
          <AlertDescription>No dead letters: every checkout reached Ordering.</AlertDescription>
        </Alert>
      ) : (
        <>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Checkout</TableHead>
                <TableHead>Event</TableHead>
                <TableHead>Error</TableHead>
                <TableHead className="text-right">Attempts</TableHead>
                <TableHead>Dead-lettered (UTC)</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((letter, index) => (
                <TableRow key={letter.messageId ?? `unreadable-${index}`}>
                  <TableCell className="font-mono text-xs">
                    {letter.isReadable ? letter.messageId : <Badge variant="destructive">Unreadable</Badge>}
                  </TableCell>
                  <TableCell className="text-muted-foreground">{letter.eventType ?? "—"}</TableCell>
                  <TableCell>
                    {letter.error ?? "—"}
                    {letter.exceptionType && <span className="block font-mono text-xs text-muted-foreground">{letter.exceptionType}</span>}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{letter.attempts ?? "—"}</TableCell>
                  <TableCell className="text-muted-foreground tabular-nums">{formatDateTime(letter.deadLetteredAtUtc)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pager
            path={OUTBOX_PATH}
            params={{}}
            pageNumber={pageNumber}
            totalPages={Math.ceil(page.total / LIMIT)}
            totalCount={page.total}
          />
        </>
      )}
    </div>
  );
}
