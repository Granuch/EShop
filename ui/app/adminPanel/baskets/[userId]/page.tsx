import Link from "next/link";
import { notFound } from "next/navigation";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Table, TableBody, TableCell, TableFooter, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { getUserBasket } from "@/lib/admin/basket";
import { formatDateTime, formatMoney } from "@/lib/admin/format";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { Basket } from "@/lib/admin/types/basket";

export const metadata = { title: "Basket · Admin · EShop" };

export default async function BasketPage({ params }: PageProps<"/adminPanel/baskets/[userId]">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "baskets.read")) return <AccessDenied />;

  const { userId } = await params;
  if (!/^[0-9A-Za-z-]{1,64}$/.test(userId)) notFound();

  // Basket lets the Admin role read anyone's basket (GET only); an unknown user simply has an empty one.
  let basket: Basket;
  try {
    basket = await getUserBasket(userId);
  } catch (error) {
    if (classifyFailure(error)?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.basket} />;
    throw error;
  }
  const items = [...basket.items].sort((a, b) => a.productName.localeCompare(b.productName));

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
            <BreadcrumbPage className="font-mono">{userId.slice(0, 8)}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader
        title="Basket"
        description={
          basket.lastModifiedAt
            ? `Last changed ${formatDateTime(basket.lastModifiedAt)} · started ${formatDateTime(basket.createdAt)}`
            : "Empty: nothing stored for this customer."
        }
      >
        {hasPermission(session, "users.read") && (
          <Link href={`/adminPanel/users/${userId}`} className="text-sm underline">
            Customer
          </Link>
        )}
      </PageHeader>

      {items.length > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Product</TableHead>
              <TableHead className="text-right">Price when added</TableHead>
              <TableHead className="text-right">Qty</TableHead>
              <TableHead className="text-right">Subtotal</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((item) => (
              <TableRow key={item.productId}>
                <TableCell>
                  <Link href={`/adminPanel/products/${item.productId}`} className="hover:underline">
                    {item.productName}
                  </Link>
                </TableCell>
                <TableCell className="text-right tabular-nums">{formatMoney(item.price)}</TableCell>
                <TableCell className="text-right tabular-nums">{item.quantity}</TableCell>
                <TableCell className="text-right tabular-nums">{formatMoney(item.subTotal)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
          <TableFooter>
            <TableRow>
              <TableCell colSpan={2}>Total</TableCell>
              <TableCell className="text-right tabular-nums">{basket.totalItems}</TableCell>
              <TableCell className="text-right tabular-nums">{formatMoney(basket.totalPrice)}</TableCell>
            </TableRow>
          </TableFooter>
        </Table>
      )}
      <p className="text-xs text-muted-foreground">Read-only: only the customer can change their basket.</p>
    </div>
  );
}
