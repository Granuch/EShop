import Link from "next/link";
import { notFound } from "next/navigation";
import { RefreshCw } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import StatusBadge, { ORDER_STATUS_TONES } from "@/components/Admin/statusBadge";
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
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission, type AdminSession } from "@/lib/admin/auth";
import { formatDateTime, formatMoney } from "@/lib/admin/format";
import { getOrder, getOrderHistory } from "@/lib/admin/ordering";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { Order, OrderStatusHistoryEntry } from "@/lib/admin/types/ordering";
import { cn } from "@/lib/utils";
import { transitionOrderAction } from "../actions";
import { ORDERS_PATH } from "../filters";
import TransitionButton from "../transitionButton";

export const metadata = { title: "Order · Admin · EShop" };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const EMPTY_GUID = "00000000-0000-0000-0000-000000000000";

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
  );
}

/** "system" for message-driven transitions; null only on rows from before actors were recorded. */
function Actor({ actorId, session }: { actorId: string | null; session: AdminSession }) {
  if (actorId === null) return <>—</>;
  if (actorId === "system") return <>System</>;
  if (actorId === session.id) return <>You</>;
  return (
    <span className="font-mono text-xs" title={actorId}>
      {actorId.slice(0, 8)}
    </span>
  );
}

function History({ entries, session }: { entries: OrderStatusHistoryEntry[]; session: AdminSession }) {
  if (entries.length === 0) return <p className="text-sm text-muted-foreground">No history.</p>;
  return (
    <ol className="space-y-3">
      {entries.map((entry) => (
        <li key={entry.id} className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 text-sm">
          <span>
            {entry.fromStatus ? `${entry.fromStatus} → ` : "Created as "}
            <span className="font-medium">{entry.toStatus}</span>
            {entry.reason && <span className="text-muted-foreground"> · {entry.reason}</span>}
          </span>
          <span className="text-xs text-muted-foreground">
            <Actor actorId={entry.actorId} session={session} /> · {formatDateTime(entry.occurredAt)}
          </span>
        </li>
      ))}
    </ol>
  );
}

function Payment({ order }: { order: Order }) {
  if (!order.paymentIntentId) return <>Not paid yet</>;
  return order.paymentIntentId.startsWith("offline:") ? (
    <>Offline · {order.paymentIntentId.slice("offline:".length)}</>
  ) : (
    <span className="font-mono text-xs">{order.paymentIntentId}</span>
  );
}

export default async function OrderDetailsPage({ params }: PageProps<"/adminPanel/orders/[orderId]">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "orders.read")) return <AccessDenied />;

  const { orderId } = await params;
  if (!GUID.test(orderId) || orderId === EMPTY_GUID) notFound();

  const [orderResult, historyResult] = await Promise.allSettled([getOrder(orderId), getOrderHistory(orderId)]);
  if (orderResult.status === "rejected") {
    const failure = classifyFailure(orderResult.reason);
    if (failure?.kind === "notFound") notFound();
    if (failure?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.ordering} />;
    if (failure?.kind === "rateLimited") {
      return (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{failure.message}</AlertDescription>
        </Alert>
      );
    }
    throw orderResult.reason;
  }
  const order = orderResult.value;
  // The history is secondary: show the order even when it cannot be read.
  if (historyResult.status === "rejected" && !classifyFailure(historyResult.reason)) throw historyResult.reason;
  const history = historyResult.status === "fulfilled" ? historyResult.value : null;

  const items = [...order.items].sort((a, b) => a.productName.localeCompare(b.productName));
  const address = order.shippingAddress;
  const detailPath = `${ORDERS_PATH}/${order.id}`;
  const canWrite = hasPermission(session, "orders.write");
  // Only the two admin transitions; Paid and Refunded come from Payment, Cancelled from the customer or Payment.
  const transition = order.status === "Paid" ? "ship" : order.status === "Shipped" ? "deliver" : null;

  return (
    <div className="space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href={ORDERS_PATH} />}>Orders</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage className="font-mono">{order.id.slice(0, 8)}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader title={`Order ${order.id.slice(0, 8)}`} description={`Created ${formatDateTime(order.createdAt)}`}>
        <StatusBadge status={order.status} tone={ORDER_STATUS_TONES[order.status]} />
        {canWrite && (
          // Kept mounted when no transition is left, so the last confirmation stays visible.
          <TransitionButton
            transition={transition}
            orderLabel={order.id.slice(0, 8)}
            action={transitionOrderAction.bind(null, order.id, transition ?? "deliver")}
          />
        )}
        {/* Paid and Refunded arrive from Payment by message, seconds later; no polling (PLAN §2.4). */}
        <Link href={detailPath} className={cn(buttonVariants({ variant: "outline", size: "sm" }))} prefetch={false}>
          <RefreshCw aria-hidden data-icon="inline-start" />
          Refresh
        </Link>
      </PageHeader>

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          <Card>
            <CardHeader>
              <CardTitle>Items</CardTitle>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Product</TableHead>
                    <TableHead className="text-right">Unit price</TableHead>
                    <TableHead className="text-right">Qty</TableHead>
                    <TableHead className="text-right">Subtotal</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>
                        <Link href={`/adminPanel/products/${item.productId}`} className="hover:underline">
                          {item.productName}
                        </Link>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{formatMoney(item.unitPrice)}</TableCell>
                      <TableCell className="text-right tabular-nums">{item.quantity}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatMoney(item.subTotal)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
                <TableFooter>
                  <TableRow>
                    <TableCell colSpan={3}>Total</TableCell>
                    <TableCell className="text-right tabular-nums">{formatMoney(order.totalPrice)}</TableCell>
                  </TableRow>
                </TableFooter>
              </Table>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Status history</CardTitle>
            </CardHeader>
            <CardContent>
              {history ? (
                <History entries={history} session={session} />
              ) : (
                <p className="text-sm text-muted-foreground">The history could not be loaded.</p>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Summary</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid grid-cols-2 gap-4">
                <Field label="Total">{formatMoney(order.totalPrice)}</Field>
                <Field label="Payment">
                  <Payment order={order} />
                </Field>
                <Field label="Paid">{formatDateTime(order.paidAt)}</Field>
                <Field label="Shipped">{formatDateTime(order.shippedAt)}</Field>
                <Field label="Delivered">{formatDateTime(order.deliveredAt)}</Field>
                {order.cancelledAt && <Field label="Cancelled">{formatDateTime(order.cancelledAt)}</Field>}
              </dl>
              {order.cancellationReason && (
                <p className="mt-4 text-sm">
                  <span className="text-muted-foreground">Cancellation reason: </span>
                  {order.cancellationReason}
                </p>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Shipping address</CardTitle>
            </CardHeader>
            <CardContent>
              <address className="text-sm not-italic leading-relaxed">
                {address.street}
                <br />
                {address.city}, {address.state} {address.zipCode}
                <br />
                {address.country}
              </address>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Customer</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="font-mono text-xs break-all">{order.userId}</p>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
