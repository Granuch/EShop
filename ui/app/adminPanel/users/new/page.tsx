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
import { listRoles } from "@/lib/admin/identity";
import { createUserAction } from "../actions";
import { USERS_PATH } from "../filters";
import { CreateUserForm } from "../userForms";

export const metadata = { title: "New user · Admin · EShop" };

export default async function NewUserPage() {
  const session = await getAdminSession();
  if (!hasPermission(session, "users.manage")) return <AccessDenied />;

  // The role list needs the Admin ROLE at Identity; without it the new account simply gets the default User role.
  let roles: string[] | null = null;
  try {
    roles = (await listRoles({ pageSize: 100 })).items.map((role) => role.name);
  } catch (error) {
    if (!classifyFailure(error)) throw error;
  }

  return (
    <div className="space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href={USERS_PATH} />}>Users</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>New</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <PageHeader title="New user" description="Creates an account on the user's behalf." />
      <CreateUserForm action={createUserAction} roles={roles} />
    </div>
  );
}
