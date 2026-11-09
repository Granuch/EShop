import Link from "next/link";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { listRoles } from "@/lib/admin/identity";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { Role } from "@/lib/admin/types/identity";
import { createRoleAction } from "./actions";
import { CreateRoleForm } from "./roleForms";

export const metadata = { title: "Roles · Admin · EShop" };

export default async function RolesPage({ searchParams }: PageProps<"/adminPanel/roles">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "users.read")) return <AccessDenied />;

  // One page of 100 holds every role in practice (two exist by default); the API's own page is 50.
  let roles: Role[];
  let total: number;
  try {
    const page = await listRoles({ pageSize: 100 });
    roles = page.items;
    total = page.totalCount;
  } catch (error) {
    const failure = classifyFailure(error);
    if (failure?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.identity} />;
    if (failure?.kind === "rateLimited") {
      return (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{failure.message}</AlertDescription>
        </Alert>
      );
    }
    throw error;
  }
  const { deleted } = await searchParams;

  return (
    <div className="space-y-6">
      <PageHeader title="Roles" description="Only Admin grants permissions; other roles are labels for now." />

      {deleted && (
        <Alert role="status">
          <AlertDescription>Role deleted. Its members lost it.</AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="min-w-0 lg:col-span-2">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Role</TableHead>
                <TableHead>Description</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {roles.map((role) => (
                <TableRow key={role.id}>
                  <TableCell>
                    <Link href={`/adminPanel/roles/${role.id}`} className="font-medium hover:underline">
                      {role.name}
                    </Link>
                  </TableCell>
                  <TableCell className="whitespace-normal text-muted-foreground">{role.description ?? "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {total > roles.length && (
            <p className="mt-2 text-xs text-muted-foreground">{`Showing the first ${roles.length} of ${total} roles.`}</p>
          )}
        </div>
        {hasPermission(session, "roles.manage") && (
          <Card>
            <CardHeader>
              <CardTitle>New role</CardTitle>
            </CardHeader>
            <CardContent>
              <CreateRoleForm action={createRoleAction} />
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  );
}
