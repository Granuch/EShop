import Link from "next/link";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import Pager from "@/components/Admin/pager";
import StatusBadge, { ORDER_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { formatDateTime, formatMoney } from "@/lib/admin/format";
import { buildAdminHref } from "@/lib/admin/href";
import { listOrders } from "@/lib/admin/ordering";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { PagedResult } from "@/lib/admin/types/common";
import type { Order } from "@/lib/admin/types/ordering";
import { hasActiveFilters, ORDERS_PATH, parseOrderFilters, toLinkParams, toOrderListQuery } from "./filters";
import OrderFilters from "./orderFilters";

export const metadata = { title: "Orders · Admin · EShop" };

/** Ids are shown shortened (PLAN Q12); the full id is in the title and on the detail page. */
function ShortId({ id }: { id: string }) {
  return (
    <span className="font-mono text-xs" title={id}>
      {id.slice(0, 8)}
    </span>
  );
}

export default async function OrdersPage({ searchParams }: PageProps<"/adminPanel/orders">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "orders.read")) return <AccessDenied />;

  const filters = parseOrderFilters(await searchParams);
  let page: PagedResult<Order> | null = null;
  let problem: string | null = null;
  try {
    page = await listOrders(toOrderListQuery(filters));
  } catch (error) {
    const failure = classifyFailure(error);
    if (failure?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.ordering} />;
    if (failure?.kind !== "invalid" && failure?.kind !== "rateLimited") throw error;
    problem = failure.message;
  }

  const linkParams = toLinkParams(filters);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Orders"
        description={page ? `${page.totalCount} ${page.totalCount === 1 ? "order" : "orders"}` : undefined}
      />

      <OrderFilters filters={filters} />

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
                <TableHead>Order</TableHead>
                <TableHead>Customer</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Items</TableHead>
                <TableHead className="text-right">Total</TableHead>
                <TableHead>Created (UTC)</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((order) => (
                <TableRow key={order.id}>
                  <TableCell>
                    <Link href={`${ORDERS_PATH}/${order.id}`} className="font-medium hover:underline">
                      <ShortId id={order.id} />
                    </Link>
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    <ShortId id={order.userId} />
                  </TableCell>
                  <TableCell>
                    <StatusBadge status={order.status} tone={ORDER_STATUS_TONES[order.status]} />
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {order.items.reduce((sum, item) => sum + item.quantity, 0)}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatMoney(order.totalPrice)}</TableCell>
                  <TableCell className="text-muted-foreground tabular-nums">{formatDateTime(order.createdAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Pager
            path={ORDERS_PATH}
            params={linkParams}
            pageNumber={page.pageNumber}
            totalPages={page.totalPages}
            totalCount={page.totalCount}
          />
        </>
      )}

      {page && page.items.length === 0 && (
        <div className="mt-12 text-center">
          {page.totalCount > 0 ? (
            <>
              <p className="font-medium">No results on this page</p>
              <Link href={buildAdminHref(ORDERS_PATH, linkParams)} className="mt-2 inline-block text-sm underline">
                Go to page 1
              </Link>
            </>
          ) : (
            <>
              <p className="font-medium">{hasActiveFilters(filters) ? "No orders match these filters" : "No orders yet"}</p>
              {hasActiveFilters(filters) && (
                <Link href={ORDERS_PATH} className="mt-2 inline-block text-sm underline">
                  Clear filters
                </Link>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}
