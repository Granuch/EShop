import Link from "next/link";
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
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { PRODUCTS_PATH } from "../filters";
import ImportForm from "./importForm";

export const metadata = { title: "Import · Products · Admin · EShop" };

export default async function ImportProductsPage() {
  const session = await getAdminSession();
  if (!hasPermission(session, "catalog.write")) return <AccessDenied />;

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
            <BreadcrumbPage>Import</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader
        title="Import products"
        description="Creates new products as drafts; it never updates existing ones. At most 1000 rows per file."
      />

      <div className="max-w-2xl space-y-2 text-sm text-muted-foreground">
        <p>
          Use the columns of <a href={`${PRODUCTS_PATH}/export`} className="underline">an export</a>: Sku, Name,
          Description, CategoryId, Price and StockQuantity are read (Description may be empty); Id, Status, DiscountPrice,
          MainImageUrl and CreatedAt are ignored. Edit an export, give each row a new SKU, and send it back.
        </p>
        <p>
          A SKU that a live product already has, or that appears twice in the file, is refused for that row; the other
          rows are still created. Imports share the bulk limit of 10 requests a minute with the bulk actions and the
          export.
        </p>
      </div>

      <ImportForm />
    </div>
  );
}
