import Link from "next/link";
import StatusBadge, { PRODUCT_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { classifyFailure } from "@/lib/admin/api";
import { getLowStock } from "@/lib/admin/catalog";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { PagedResult } from "@/lib/admin/types/common";
import type { Product } from "@/lib/admin/types/catalog";

const THRESHOLD = 10;
const SHOWN = 5;

function Shell({ children, description }: { children: React.ReactNode; description?: React.ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle id="low-stock-heading">Low stock</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

export function LowStockCardSkeleton() {
  return (
    <Shell>
      <div aria-busy="true" className="space-y-2">
        {Array.from({ length: SHOWN }, (_, i) => (
          <Skeleton key={i} className="h-6 w-full" />
        ))}
      </div>
    </Shell>
  );
}

/**
 * Products with fewer than 10 in stock, drafts included (an unpublished product that is out of stock is exactly what
 * needs seeing before it is published). The API sorts by name; the first five are shown. Never throws.
 */
export default async function LowStockCard() {
  let page: PagedResult<Product>;
  try {
    page = await getLowStock({ threshold: THRESHOLD, pageSize: SHOWN });
  } catch (error) {
    const failure = classifyFailure(error);
    const message =
      failure?.kind === "forbidden"
        ? `This account may not read stock levels. ${ADMIN_ROLE_HINTS.catalog}`
        : failure?.kind === "rateLimited" || failure?.kind === "invalid"
          ? failure.message
          : "The catalog service could not be reached.";
    return (
      <Shell>
        <p role="alert" className="text-sm text-destructive">
          Could not load stock levels. {message}
        </p>
      </Shell>
    );
  }

  return (
    <Shell description={`Fewer than ${THRESHOLD} in stock, drafts included`}>
      {page.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">Every product has at least {THRESHOLD} in stock.</p>
      ) : (
        <>
          <ul className="divide-y">
            {page.items.map((product) => (
              <li key={product.id} className="flex items-center justify-between gap-4 py-1.5 text-sm">
                <span className="flex min-w-0 items-center gap-2">
                  <Link href={`/adminPanel/products/${product.id}`} className="truncate hover:underline">
                    {product.name}
                  </Link>
                  {product.status !== "Active" && (
                    <StatusBadge status={product.status} tone={PRODUCT_STATUS_TONES[product.status]} />
                  )}
                </span>
                <span
                  className={`shrink-0 tabular-nums ${product.stockQuantity === 0 ? "font-medium text-destructive" : "text-muted-foreground"}`}
                >
                  {product.stockQuantity === 0 ? "Out of stock" : product.stockQuantity}
                </span>
              </li>
            ))}
          </ul>
          <Link
            href={`/adminPanel/products?stockBelow=${THRESHOLD}`}
            className="mt-3 inline-block text-sm underline"
          >
            {page.totalCount > SHOWN ? `View all ${page.totalCount}` : "View in products"}
          </Link>
        </>
      )}
    </Shell>
  );
}
