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
import { flattenCategories, getCategoryTree, toCategoryChoices } from "@/lib/admin/catalog";
import { createProductAction } from "../actions";
import { PRODUCTS_PATH } from "../filters";
import ProductForm from "../productForm";

export const metadata = { title: "New product · Admin · EShop" };

export default async function NewProductPage() {
  const session = await getAdminSession();
  if (!hasPermission(session, "catalog.write")) return <AccessDenied />;

  // Without the tree there is nothing to choose a category from, so a failure here goes to error.tsx.
  const categories = toCategoryChoices(flattenCategories(await getCategoryTree()));

  return (
    <div className="max-w-3xl space-y-6">
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
            <BreadcrumbPage>New product</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <PageHeader title="New product" description="Created as a draft: publish it from its page when it is ready." />
      <ProductForm mode="create" action={createProductAction} categories={categories} />
    </div>
  );
}
