import Link from "next/link";
import { Info, Plus } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import Pager from "@/components/Admin/pager";
import StatusBadge, { PRODUCT_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { flattenCategories, getCategoryTree, listProducts, type CategoryOption } from "@/lib/admin/catalog";
import { formatDate, formatMoney } from "@/lib/admin/format";
import { buildAdminHref } from "@/lib/admin/href";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { PagedResult } from "@/lib/admin/types/common";
import type { Product } from "@/lib/admin/types/catalog";
import { hasActiveFilters, parseProductFilters, PRODUCTS_PATH, toLinkParams, toProductListQuery } from "./filters";
import ProductFilters from "./productFilters";

export const metadata = { title: "Products · Admin · EShop" };

function PriceCell({ product }: { product: Product }) {
  if (product.discountPrice === null) return <>{formatMoney(product.price)}</>;
  return (
    <span className="flex flex-col items-end leading-tight">
      <span>{formatMoney(product.discountPrice)}</span>
      <s className="text-xs text-muted-foreground">{formatMoney(product.price)}</s>
    </span>
  );
}

function Thumbnail({ url }: { url: string | null }) {
  if (!url) return <span aria-hidden className="size-10 shrink-0 rounded-md bg-muted" />;
  // A plain <img>: product images may come from any host, and next.config only allows picsum (PLAN Q7).
  // eslint-disable-next-line @next/next/no-img-element
  return <img src={url} alt="" loading="lazy" className="size-10 shrink-0 rounded-md bg-muted object-cover" />;
}

export default async function ProductsPage({ searchParams }: PageProps<"/adminPanel/products">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "catalog.read")) return <AccessDenied />;

  const filters = parseProductFilters(await searchParams);
  const [listResult, treeResult] = await Promise.allSettled([
    listProducts(toProductListQuery(filters)),
    getCategoryTree(),
  ]);

  let page: PagedResult<Product> | null = null;
  let problem: string | null = null;
  if (listResult.status === "fulfilled") {
    page = listResult.value;
  } else {
    const failure = classifyFailure(listResult.reason);
    if (failure?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.catalog} />;
    if (failure?.kind !== "invalid" && failure?.kind !== "rateLimited") throw listResult.reason;
    problem = failure.message;
  }

  // The tree only names categories and fills the picker, so its failure must not hide the list.
  let categories: CategoryOption[] | null = null;
  if (treeResult.status === "fulfilled") {
    categories = flattenCategories(treeResult.value);
  } else if (!classifyFailure(treeResult.reason)) {
    throw treeResult.reason;
  }
  const categoryNames = new Map(categories?.map((category) => [category.id, category]) ?? []);

  const linkParams = toLinkParams(filters);
  const filtered = hasActiveFilters(filters);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Products"
        description={page ? `${page.totalCount} ${page.totalCount === 1 ? "product" : "products"}` : undefined}
      >
        {hasPermission(session, "catalog.write") && (
          <Link href={`${PRODUCTS_PATH}/new`} className={buttonVariants()}>
            <Plus aria-hidden data-icon="inline-start" />
            New product
          </Link>
        )}
      </PageHeader>

      {!session.roles.includes("Admin") && (
        <Alert>
          <Info aria-hidden />
          <AlertDescription>
            Drafts are visible only to the Admin role, so this list may be missing unpublished products.
          </AlertDescription>
        </Alert>
      )}

      <ProductFilters filters={filters} categories={categories} />

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
                <TableHead>Product</TableHead>
                <TableHead>Category</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Price</TableHead>
                <TableHead className="text-right">Stock</TableHead>
                <TableHead>Created (UTC)</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((product) => {
                const category = categoryNames.get(product.categoryId);
                return (
                  <TableRow key={product.id}>
                    <TableCell>
                      <div className="flex items-center gap-3">
                        <Thumbnail url={product.mainImageUrl} />
                        <div className="min-w-0">
                          <Link
                            href={`${PRODUCTS_PATH}/${product.id}`}
                            className="block max-w-64 truncate font-medium hover:underline"
                          >
                            {product.name}
                          </Link>
                          <span className="font-mono text-xs text-muted-foreground">{product.sku}</span>
                        </div>
                      </div>
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {category ? (
                        <span title={category.path}>
                          {category.name}
                          {!category.isActive && " (deleted)"}
                        </span>
                      ) : (
                        <span className="font-mono text-xs">{product.categoryId.slice(0, 8)}</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <StatusBadge status={product.status} tone={PRODUCT_STATUS_TONES[product.status]} />
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      <PriceCell product={product} />
                    </TableCell>
                    <TableCell
                      className={`text-right tabular-nums ${product.stockQuantity === 0 ? "font-medium text-destructive" : ""}`}
                    >
                      {product.stockQuantity}
                    </TableCell>
                    <TableCell className="text-muted-foreground tabular-nums">{formatDate(product.createdAt)}</TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
          <Pager
            path={PRODUCTS_PATH}
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
              <Link href={buildAdminHref(PRODUCTS_PATH, linkParams)} className="mt-2 inline-block text-sm underline">
                Go to page 1
              </Link>
            </>
          ) : (
            <>
              <p className="font-medium">{filtered ? "No products match these filters" : "No products yet"}</p>
              {filtered && (
                <Link href={PRODUCTS_PATH} className="mt-2 inline-block text-sm underline">
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
