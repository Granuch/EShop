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
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { flattenCategories, getCategoryTree, toCategoryChoices } from "@/lib/admin/catalog";
import { createCategoryAction } from "../actions";
import CategoryForm from "../categoryForm";

export const metadata = { title: "New category · Admin · EShop" };

export default async function NewCategoryPage({ searchParams }: PageProps<"/adminPanel/categories/new">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "catalog.write")) return <AccessDenied />;

  const { parent } = await searchParams;
  let parents: { id: string; label: string }[] | undefined;
  try {
    parents = toCategoryChoices(flattenCategories(await getCategoryTree()));
  } catch (error) {
    if (!classifyFailure(error)) throw error;
  }
  const parentId = typeof parent === "string" && parents?.some((option) => option.id === parent) ? parent : "";

  return (
    <div className="space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel/categories" />}>Categories</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>New</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <PageHeader title="New category" />
      <CategoryForm
        mode="create"
        action={createCategoryAction}
        parents={parents}
        initial={{ name: "", slug: "", description: "", displayOrder: "", parentCategoryId: parentId }}
      />
    </div>
  );
}
