import Link from "next/link";
import { RotateCcw } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import ActionButton from "@/components/Admin/actionButton";
import PageHeader from "@/components/Admin/pageHeader";
import Pager from "@/components/Admin/pager";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { flattenCategories, getCategoryTree, listDeletedProducts } from "@/lib/admin/catalog";
import { formatDate, formatMoney } from "@/lib/admin/format";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { PagedResult } from "@/lib/admin/types/common";
import type { Product } from "@/lib/admin/types/catalog";
import { restoreProductAction } from "../actions";
import { DEFAULT_PAGE_SIZE, PRODUCTS_PATH } from "../filters";

export const metadata = { title: "Recycle bin · Products · Admin · EShop" };

const DELETED_PATH = `${PRODUCTS_PATH}/deleted`;

function first(value: string | string[] | undefined): string {
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? "";
}

export default async function DeletedProductsPage({ searchParams }: PageProps<"/adminPanel/products/deleted">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "catalog.read")) return <AccessDenied />;

  const params = await searchParams;
  const searchTerm = first(params.searchTerm).slice(0, 200);
  const pageParam = first(params.pageNumber);
  const pageNumber = /^\d{1,9}$/.test(pageParam) && Number(pageParam) >= 1 ? Number(pageParam) : 1;

  const [listResult, treeResult] = await Promise.allSettled([
    listDeletedProducts({ pageNumber, pageSize: DEFAULT_PAGE_SIZE, searchTerm: searchTerm || undefined }),
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
  if (treeResult.status === "rejected" && !classifyFailure(treeResult.reason)) throw treeResult.reason;
  const categories = new Map(
    (treeResult.status === "fulfilled" ? flattenCategories(treeResult.value) : []).map((c) => [c.id, c]),
  );
  const canWrite = hasPermission(session, "catalog.write");

  return (
    <div className="space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href={PRODUCTS_PATH} />}>Products</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>Recycle bin</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader
        title="Recycle bin"
        description={
          page
            ? `${page.totalCount} deleted ${page.totalCount === 1 ? "product" : "products"}. Newest created first; the API keeps no deletion date.`
            : undefined
        }
      />

      <form action={DELETED_PATH} role="search" aria-label="Search deleted products" className="flex items-end gap-3">
        <div className="flex w-full flex-col gap-1 sm:w-64">
          <label htmlFor="searchTerm" className="text-xs font-medium text-muted-foreground">
            Search name or SKU
          </label>
          <Input id="searchTerm" name="searchTerm" type="search" defaultValue={searchTerm} maxLength={200} />
        </div>
        <Button type="submit" variant="outline">
          Search
        </Button>
        {searchTerm && (
          <Link href={DELETED_PATH} className="pb-1.5 text-sm underline">
            Clear
          </Link>
        )}
      </form>

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
                <TableHead className="text-right">Price</TableHead>
                <TableHead>Created (UTC)</TableHead>
                {canWrite && <TableHead className="text-right">Restore</TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((product) => {
                const category = categories.get(product.categoryId);
                return (
                  <TableRow key={product.id}>
                    <TableCell>
                      <span className="block max-w-64 truncate font-medium">{product.name}</span>
                      <span className="font-mono text-xs text-muted-foreground">{product.sku}</span>
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
                    <TableCell className="text-right tabular-nums">{formatMoney(product.price)}</TableCell>
                    <TableCell className="text-muted-foreground tabular-nums">{formatDate(product.createdAt)}</TableCell>
                    {canWrite && (
                      <TableCell className="text-right whitespace-normal">
                        <ActionButton action={restoreProductAction.bind(null, product.id)} label={`Restore ${product.name}`}>
                          <RotateCcw aria-hidden data-icon="inline-start" />
                          Restore
                        </ActionButton>
                      </TableCell>
                    )}
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
          <Pager
            path={DELETED_PATH}
            params={{ searchTerm: searchTerm || undefined }}
            pageNumber={page.pageNumber}
            totalPages={page.totalPages}
            totalCount={page.totalCount}
          />
        </>
      )}

      {page && page.items.length === 0 && (
        <p className="mt-12 text-center font-medium">
          {searchTerm ? "No deleted products match this search" : "The recycle bin is empty"}
        </p>
      )}
    </div>
  );
}
