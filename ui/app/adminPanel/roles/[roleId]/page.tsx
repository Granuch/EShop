import Link from "next/link";
import { notFound } from "next/navigation";
import { Trash2, UserMinus } from "lucide-react";
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
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { getRole, listRoleUsers } from "@/lib/admin/identity";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { Role } from "@/lib/admin/types/identity";
import { addMemberAction, deleteRoleAction, removeMemberAction, updateRoleAction } from "../actions";
import { AddMemberForm, DescriptionForm } from "../roleForms";

export const metadata = { title: "Role · Admin · EShop" };

const ROLES_PATH = "/adminPanel/roles";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** Identity refuses to delete these (400 Role.CannotDelete). */
const BUILT_IN = ["admin", "user"];
const PAGE_SIZE = 50;

export default async function RolePage({ params, searchParams }: PageProps<"/adminPanel/roles/[roleId]">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "users.read")) return <AccessDenied />;

  const { roleId } = await params;
  // GET /roles/{id} takes the id; a name ("Admin") is a 404 there.
  if (!GUID.test(roleId)) notFound();
  const pageParam = (await searchParams).pageNumber;
  const pageNumber = typeof pageParam === "string" && /^\d{1,6}$/.test(pageParam) && Number(pageParam) > 0 ? Number(pageParam) : 1;

  let role: Role;
  try {
    role = await getRole(roleId);
  } catch (error) {
    const failure = classifyFailure(error);
    if (failure?.kind === "notFound") notFound();
    if (failure?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.identity} />;
    throw error;
  }

  let members;
  let membersProblem: string | null = null;
  try {
    members = await listRoleUsers(role.name, { pageNumber, pageSize: PAGE_SIZE });
  } catch (error) {
    const failure = classifyFailure(error);
    if (!failure) throw error;
    membersProblem = failure.kind === "rateLimited" || failure.kind === "invalid" ? failure.message : "The members could not be loaded.";
  }
  const canManage = hasPermission(session, "roles.manage");
  const builtIn = BUILT_IN.includes(role.name.toLowerCase());
  const isAdminRole = role.name.toLowerCase() === "admin";

  return (
    <div className="space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href={ROLES_PATH} />}>Roles</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>{role.name}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader title={role.name} description={role.description ?? "No description"}>
        {canManage && !builtIn && (
          <ActionButton
            action={deleteRoleAction.bind(null, role.id)}
            variant="destructive"
            size="default"
            confirm={{
              title: `Delete the role "${role.name}"?`,
              description: "Every member loses it. This cannot be undone.",
              confirmLabel: "Delete role",
            }}
          >
            <Trash2 aria-hidden data-icon="inline-start" />
            Delete
          </ActionButton>
        )}
      </PageHeader>

      {isAdminRole && (
        <Alert>
          <AlertDescription>
            Admin holds every permission. Members get the whole admin panel; changes reach a member at their next
            sign-in or token refresh.
          </AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="min-w-0 lg:col-span-2">
          <CardHeader>
            <CardTitle>{members ? `Members (${members.totalCount})` : "Members"}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {membersProblem && (
              <p role="alert" className="text-sm text-destructive">
                {membersProblem}
              </p>
            )}
            {members && members.items.length === 0 && <p className="text-sm text-muted-foreground">No members.</p>}
            {members && members.items.length > 0 && (
              <>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>User</TableHead>
                      <TableHead>Email</TableHead>
                      {canManage && (
                        <TableHead className="w-10">
                          <span className="sr-only">Remove</span>
                        </TableHead>
                      )}
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {members.items.map((member) => (
                      <TableRow key={member.id}>
                        <TableCell>
                          <Link href={`/adminPanel/users/${member.id}`} className="hover:underline">
                            {`${member.firstName} ${member.lastName}`.trim() || member.email}
                          </Link>
                          {member.id === session.id && <span className="text-muted-foreground"> (you)</span>}
                        </TableCell>
                        <TableCell className="text-muted-foreground">{member.email}</TableCell>
                        {canManage && (
                          <TableCell className="whitespace-normal">
                            <ActionButton
                              action={removeMemberAction.bind(null, role.id, role.name, member.id)}
                              label={`Remove ${member.email} from ${role.name}`}
                              size="icon-sm"
                              variant="ghost"
                              confirm={
                                isAdminRole
                                  ? {
                                      title: `Remove ${member.email} from Admin?`,
                                      description: "They lose every admin permission at their next token refresh.",
                                      confirmLabel: "Remove",
                                    }
                                  : undefined
                              }
                            >
                              <UserMinus aria-hidden />
                            </ActionButton>
                          </TableCell>
                        )}
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
                <Pager
                  path={`${ROLES_PATH}/${role.id}`}
                  params={{}}
                  pageNumber={members.pageNumber}
                  totalPages={members.totalPages}
                  totalCount={members.totalCount}
                />
              </>
            )}
          </CardContent>
        </Card>

        {canManage && (
          <div className="min-w-0 space-y-6">
            <Card>
              <CardHeader>
                <CardTitle>Add a member</CardTitle>
              </CardHeader>
              <CardContent>
                <AddMemberForm action={addMemberAction.bind(null, role.id, role.name)} />
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>Description</CardTitle>
              </CardHeader>
              <CardContent>
                <DescriptionForm action={updateRoleAction.bind(null, role.id)} description={role.description ?? ""} />
              </CardContent>
            </Card>
          </div>
        )}
      </div>
    </div>
  );
}
