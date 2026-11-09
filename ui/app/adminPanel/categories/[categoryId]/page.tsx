import Link from "next/link";
import { notFound } from "next/navigation";
import { FolderPlus, ListFilter, RotateCcw, Trash2 } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import ActionButton from "@/components/Admin/actionButton";
import PageHeader from "@/components/Admin/pageHeader";
import StatusBadge, { PRODUCT_STATUS_TONES } from "@/components/Admin/statusBadge";
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
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import {
  flattenCategories,
  getCategory,
  getCategoryProducts,
  getCategoryStats,
  getCategoryTree,
  toCategoryChoices,
} from "@/lib/admin/catalog";
import { formatMoney } from "@/lib/admin/format";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { Category } from "@/lib/admin/types/catalog";
import { cn } from "@/lib/utils";
import { deleteCategoryAction, moveCategoryParentAction, restoreCategoryAction, updateCategoryAction } from "../actions";
import CategoryForm from "../categoryForm";
import MoveForm from "../moveForm";

export const metadata = { title: "Category · Admin · EShop" };

const CATEGORIES_PATH = "/adminPanel/categories";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const PREVIEW = 10;

/** The node and its ancestors, root first, from the admin tree (which also holds deleted categories). */
function locate(nodes: Category[], id: string, trail: Category[] = []): Category[] | null {
  for (const node of nodes) {
    if (node.id === id) return [...trail, node];
    const found = locate(node.childCategories, id, [...trail, node]);
    if (found) return found;
  }
  return null;
}

function subtreeIds(node: Category): Set<string> {
  const ids = new Set([node.id]);
  for (const child of node.childCategories) for (const id of subtreeIds(child)) ids.add(id);
  return ids;
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-lg font-semibold tabular-nums">{value}</dd>
    </div>
  );
}

function failureText(reason: unknown): string {
  const failure = classifyFailure(reason);
  if (failure?.kind === "forbidden") return ADMIN_ROLE_HINTS.catalog;
  if (failure?.kind === "rateLimited" || failure?.kind === "invalid") return failure.message;
  return "The catalog service could not be reached.";
}

export default async function CategoryPage({ params }: PageProps<"/adminPanel/categories/[categoryId]">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "catalog.read")) return <AccessDenied />;

  const { categoryId } = await params;
  if (!GUID.test(categoryId) || /^0{8}-/.test(categoryId)) notFound();

  let tree: Category[];
  try {
    tree = await getCategoryTree();
  } catch (error) {
    if (classifyFailure(error)?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.catalog} />;
    throw error;
  }
  const trail = locate(tree, categoryId);
  if (!trail) notFound();
  const node = trail[trail.length - 1];
  const parent = trail.length > 1 ? trail[trail.length - 2] : null;
  const canWrite = hasPermission(session, "catalog.write");

  const breadcrumb = (
    <Breadcrumb>
      <BreadcrumbList>
        <BreadcrumbItem>
          <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
        </BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          <BreadcrumbLink render={<Link href={CATEGORIES_PATH} />}>Categories</BreadcrumbLink>
        </BreadcrumbItem>
        {trail.slice(0, -1).map((ancestor) => (
          <span key={ancestor.id} className="contents">
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbLink render={<Link href={`${CATEGORIES_PATH}/${ancestor.id}`} />}>{ancestor.name}</BreadcrumbLink>
            </BreadcrumbItem>
          </span>
        ))}
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          <BreadcrumbPage>{node.name}</BreadcrumbPage>
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  );

  // A deleted category is only in the admin tree: every other read answers 404 for it.
  if (!node.isActive) {
    return (
      <div className="space-y-6">
        {breadcrumb}
        <PageHeader title={node.name} description={`Slug ${node.slug}`}>
          <Badge variant="outline">Deleted</Badge>
          {canWrite && (
            <ActionButton action={restoreCategoryAction.bind(null, node.id)} variant="default" size="default">
              <RotateCcw aria-hidden data-icon="inline-start" />
              Restore
            </ActionButton>
          )}
        </PageHeader>
        <p className="text-sm text-muted-foreground">
          Deleted categories are hidden from the shop and cannot be edited. Restoring puts it back at its old place
          {parent ? `, under "${parent.name}"` : ", at the top level"}.
        </p>
      </div>
    );
  }

  const [detailResult, statsResult, productsResult] = await Promise.allSettled([
    getCategory(node.id),
    getCategoryStats(node.id),
    getCategoryProducts(node.id, { pageSize: PREVIEW }),
  ]);
  // The tree said live a moment ago; a 404 now means it was deleted meanwhile.
  if (detailResult.status === "rejected" && classifyFailure(detailResult.reason)?.kind === "notFound") notFound();
  const detail = detailResult.status === "fulfilled" ? detailResult.value : node;
  const excluded = subtreeIds(node);
  const parents = toCategoryChoices(flattenCategories(tree)).filter((option) => !excluded.has(option.id));
  const children = detail.childCategories;

  return (
    <div className="space-y-6">
      {breadcrumb}
      <PageHeader title={detail.name} description={`Slug ${detail.slug}${parent ? ` · in ${parent.name}` : " · top level"}`}>
        <Link href={`/adminPanel/products?categoryId=${node.id}`} className={cn(buttonVariants({ variant: "outline" }))}>
          <ListFilter aria-hidden data-icon="inline-start" />
          Products
        </Link>
        {canWrite && (
          <>
            <Link href={`${CATEGORIES_PATH}/new?parent=${node.id}`} className={cn(buttonVariants({ variant: "outline" }))}>
              <FolderPlus aria-hidden data-icon="inline-start" />
              Add subcategory
            </Link>
            <ActionButton
              action={deleteCategoryAction.bind(null, node.id)}
              variant="destructive"
              size="default"
              confirm={{
                title: "Delete this category?",
                description: `"${detail.name}" leaves the shop and its slug becomes free. Only an empty category can be deleted: no live subcategories and no live products, drafts included. It can be restored later.`,
                confirmLabel: "Delete category",
              }}
            >
              <Trash2 aria-hidden data-icon="inline-start" />
              Delete
            </ActionButton>
          </>
        )}
      </PageHeader>

      {detailResult.status === "rejected" && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{`Showing the tree's copy. ${failureText(detailResult.reason)}`}</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          <Card>
            <CardHeader>
              <CardTitle>{canWrite ? "Edit details" : "Details"}</CardTitle>
            </CardHeader>
            <CardContent>
              {canWrite ? (
                <CategoryForm
                  mode="edit"
                  action={updateCategoryAction.bind(null, node.id)}
                  initial={{
                    name: detail.name,
                    slug: detail.slug,
                    description: detail.description ?? "",
                    displayOrder: String(detail.displayOrder),
                  }}
                />
              ) : (
                <p className="text-sm whitespace-pre-line [overflow-wrap:anywhere]">
                  {detail.description || <span className="text-muted-foreground">No description</span>}
                </p>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Products</CardTitle>
            </CardHeader>
            <CardContent>
              {productsResult.status === "rejected" ? (
                <p role="alert" className="text-sm text-destructive">
                  {`Could not load products. ${failureText(productsResult.reason)}`}
                </p>
              ) : productsResult.value.items.length === 0 ? (
                <p className="text-sm text-muted-foreground">No products here or in its subcategories.</p>
              ) : (
                <>
                  <ul className="divide-y">
                    {productsResult.value.items.map((product) => (
                      <li key={product.id} className="flex items-center justify-between gap-4 py-1.5 text-sm">
                        <span className="flex min-w-0 items-center gap-2">
                          <Link href={`/adminPanel/products/${product.id}`} className="truncate hover:underline">
                            {product.name}
                          </Link>
                          {product.status !== "Active" && (
                            <StatusBadge status={product.status} tone={PRODUCT_STATUS_TONES[product.status]} />
                          )}
                        </span>
                        <span className="shrink-0 tabular-nums text-muted-foreground">
                          {formatMoney(product.discountPrice ?? product.price)}
                        </span>
                      </li>
                    ))}
                  </ul>
                  <Link href={`/adminPanel/products?categoryId=${node.id}`} className="mt-3 inline-block text-sm underline">
                    {productsResult.value.totalCount > PREVIEW
                      ? `View all ${productsResult.value.totalCount}`
                      : "View in products"}
                  </Link>
                </>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Statistics</CardTitle>
            </CardHeader>
            <CardContent>
              {statsResult.status === "fulfilled" ? (
                <>
                  <dl className="grid grid-cols-2 gap-4">
                    <Stat label="Products" value={statsResult.value.productCount} />
                    <Stat label="Published" value={statsResult.value.publishedProductCount} />
                    <Stat label="Units in stock" value={statsResult.value.totalStock} />
                    <Stat label="Out of stock" value={statsResult.value.outOfStockCount} />
                    <Stat label="Subcategories" value={statsResult.value.childCategoryCount} />
                  </dl>
                  <p className="mt-3 text-xs text-muted-foreground">Product counts include every subcategory.</p>
                </>
              ) : (
                <p role="alert" className="text-sm text-destructive">
                  {`Could not load statistics. ${failureText(statsResult.reason)}`}
                </p>
              )}
            </CardContent>
          </Card>

          {canWrite && (
            <Card>
              <CardHeader>
                <CardTitle>Move</CardTitle>
              </CardHeader>
              <CardContent>
                <MoveForm action={moveCategoryParentAction.bind(null, node.id)} parents={parents} currentParentId={parent?.id ?? null} />
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle>Subcategories</CardTitle>
            </CardHeader>
            <CardContent>
              {children.length === 0 ? (
                <p className="text-sm text-muted-foreground">None</p>
              ) : (
                <ul className="space-y-1 text-sm">
                  {children.map((child) => (
                    <li key={child.id}>
                      <Link href={`${CATEGORIES_PATH}/${child.id}`} className="hover:underline">
                        {child.name}
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
