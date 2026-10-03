import Link from "next/link";
import { notFound } from "next/navigation";
import { ExternalLink } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import StatusBadge, { PRODUCT_STATUS_TONES } from "@/components/Admin/statusBadge";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { flattenCategories, getCategoryTree, getProduct, toCategoryChoices } from "@/lib/admin/catalog";
import { formatDateTime, formatMoney } from "@/lib/admin/format";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { ProductDetails } from "@/lib/admin/types/catalog";
import { updateProductAction } from "../actions";
import { PRODUCTS_PATH } from "../filters";
import ProductForm from "../productForm";

export const metadata = { title: "Product · Admin · EShop" };

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

export default async function ProductDetailsPage({ params }: PageProps<"/adminPanel/products/[productId]">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "catalog.read")) return <AccessDenied />;

  const { productId } = await params;
  // A non-GUID would be a bare 404 at the gateway, and the all-zero id a 400: neither is worth a call.
  if (!GUID.test(productId) || productId === EMPTY_GUID) notFound();

  const [productResult, treeResult] = await Promise.allSettled([getProduct(productId), getCategoryTree()]);

  if (productResult.status === "rejected") {
    const failure = classifyFailure(productResult.reason);
    if (failure?.kind === "notFound") notFound();
    if (failure?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.catalog} />;
    if (failure?.kind === "rateLimited") {
      return (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{failure.message}</AlertDescription>
        </Alert>
      );
    }
    throw productResult.reason;
  }
  const product: ProductDetails = productResult.value;

  if (treeResult.status === "rejected" && !classifyFailure(treeResult.reason)) throw treeResult.reason;
  const categoryOptions = treeResult.status === "fulfilled" ? flattenCategories(treeResult.value) : null;
  const category = categoryOptions?.find((option) => option.id === product.categoryId);
  const canWrite = hasPermission(session, "catalog.write");

  const attributes = [...product.attributes].sort((a, b) => a.name.localeCompare(b.name));

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
            <BreadcrumbPage className="max-w-64 truncate">{product.name}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader title={product.name} description={`SKU ${product.sku}`}>
        <StatusBadge status={product.status} tone={PRODUCT_STATUS_TONES[product.status]} />
        {product.status === "Active" && (
          <Link
            href={`/product/${product.id}`}
            className="inline-flex items-center gap-1 text-sm underline"
            target="_blank"
            rel="noreferrer"
          >
            View in shop
            <ExternalLink aria-hidden className="size-3.5" />
            <span className="sr-only">(opens in a new tab)</span>
          </Link>
        )}
      </PageHeader>

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>{canWrite ? "Edit details" : "Details"}</CardTitle>
          </CardHeader>
          <CardContent>
            {canWrite ? (
              <ProductForm
                mode="edit"
                action={updateProductAction.bind(null, product.id)}
                categories={
                  categoryOptions
                    ? toCategoryChoices(categoryOptions, product.categoryId)
                    : [{ id: product.categoryId, label: "Current category (list unavailable)" }]
                }
                initial={{
                  name: product.name,
                  sku: product.sku,
                  price: String(product.price),
                  categoryId: product.categoryId,
                  description: product.description ?? "",
                }}
              />
            ) : (
              <>
                <dl className="grid gap-4 sm:grid-cols-2">
                  <Field label="Category">
                    {category ? `${category.path}${category.isActive ? "" : " (deleted)"}` : product.categoryId}
                  </Field>
                  <Field label="Price">{formatMoney(product.price)}</Field>
                </dl>
                <div className="mt-6">
                  <h2 className="text-xs font-medium text-muted-foreground">Description</h2>
                  <p className="mt-0.5 text-sm whitespace-pre-line">
                    {product.description || <span className="text-muted-foreground">No description</span>}
                  </p>
                </div>
              </>
            )}
          </CardContent>
        </Card>

        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>Stock and pricing</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid grid-cols-2 gap-4">
                <Field label="Stock">
                  <span className={product.stockQuantity === 0 ? "font-medium text-destructive" : undefined}>
                    {product.stockQuantity === 0 ? "Out of stock" : product.stockQuantity}
                  </span>
                </Field>
                <Field label="Customers pay">{formatMoney(product.discountPrice ?? product.price)}</Field>
                <Field label="Discount price">
                  {product.discountPrice === null ? "None" : formatMoney(product.discountPrice)}
                </Field>
                <Field label="Created">{formatDateTime(product.createdAt)}</Field>
              </dl>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Attributes</CardTitle>
            </CardHeader>
            <CardContent>
              {attributes.length === 0 ? (
                <p className="text-sm text-muted-foreground">No attributes</p>
              ) : (
                <dl className="space-y-2">
                  {attributes.map((attribute) => (
                    <div key={attribute.id} className="flex justify-between gap-4 text-sm">
                      <dt className="text-muted-foreground">{attribute.name}</dt>
                      <dd className="text-right">{attribute.value}</dd>
                    </div>
                  ))}
                </dl>
              )}
            </CardContent>
          </Card>
        </div>
      </div>

      <section aria-labelledby="images-heading">
        <h2 id="images-heading" className="text-sm font-semibold">
          Images
        </h2>
        {product.images.length === 0 ? (
          <p className="mt-2 text-sm text-muted-foreground">No images</p>
        ) : (
          <ul className="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
            {product.images.map((image) => (
              <li key={image.id} className="space-y-1">
                {/* A plain <img>: images may come from any host; next.config only allows picsum (PLAN Q7). */}
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={image.url}
                  alt={image.altText ?? ""}
                  loading="lazy"
                  className="aspect-square w-full rounded-lg bg-muted object-cover ring-1 ring-foreground/10"
                />
                <p className="truncate text-xs text-muted-foreground">
                  {image.isMain && <span className="font-medium text-foreground">Main · </span>}
                  {image.altText || "No alt text"}
                </p>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
