import Link from "next/link";
import { Plus } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import Pager from "@/components/Admin/pager";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { endOfDayUtc, formatDate, formatDateTime, startOfDayUtc } from "@/lib/admin/format";
import { buildAdminHref } from "@/lib/admin/href";
import { getAdminUserStats, listAdminUsers, listRoles } from "@/lib/admin/identity";
import type { PagedResult } from "@/lib/admin/types/common";
import type { AdminUser, AdminUserStats } from "@/lib/admin/types/identity";
import { cn } from "@/lib/utils";
import { hasActiveFilters, parseUserFilters, toLinkParams, toUserListQuery, USERS_PATH } from "./filters";
import UserFilters from "./userFilters";
import UserStateBadges from "./userStateBadges";

export const metadata = { title: "Users · Admin · EShop" };

const NEW_DAYS = 30;

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="text-lg font-semibold tabular-nums">{value}</dd>
    </div>
  );
}

function statsWindow(now = new Date()) {
  const day = (offset: number) => new Date(now.getTime() - offset * 86_400_000).toISOString().slice(0, 10);
  return { from: startOfDayUtc(day(NEW_DAYS - 1))!, to: endOfDayUtc(day(0))! };
}

export default async function UsersPage({ searchParams }: PageProps<"/adminPanel/users">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "users.read")) return <AccessDenied />;

  const filters = parseUserFilters(await searchParams);
  const [listResult, statsResult, rolesResult] = await Promise.allSettled([
    listAdminUsers(toUserListQuery(filters)),
    getAdminUserStats(statsWindow()),
    listRoles({ pageSize: 100 }),
  ]);

  let page: PagedResult<AdminUser> | null = null;
  let problem: string | null = null;
  if (listResult.status === "fulfilled") {
    page = listResult.value;
  } else {
    const failure = classifyFailure(listResult.reason);
    if (failure?.kind === "forbidden") return <AccessDenied />;
    if (failure?.kind !== "invalid" && failure?.kind !== "rateLimited") throw listResult.reason;
    problem = failure.message;
  }
  const stats: AdminUserStats | null = statsResult.status === "fulfilled" ? statsResult.value : null;
  // The role list needs the Admin ROLE at Identity; without it the filter offers only the role already chosen.
  const roles = rolesResult.status === "fulfilled" ? rolesResult.value.items.map((role) => role.name) : null;
  const linkParams = toLinkParams(filters);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Users"
        description={page ? `${page.totalCount} ${page.totalCount === 1 ? "user" : "users"}` : undefined}
      >
        {hasPermission(session, "users.manage") && (
          <Link href={`${USERS_PATH}/new`} className={cn(buttonVariants())}>
            <Plus aria-hidden data-icon="inline-start" />
            New user
          </Link>
        )}
      </PageHeader>

      {stats && (
        <dl className="grid grid-cols-3 gap-4 rounded-lg border p-4 sm:grid-cols-6" aria-label="User statistics">
          <Stat label="Total" value={stats.total} />
          <Stat label={`New, ${NEW_DAYS} days`} value={stats.newInPeriod} />
          <Stat label="Active" value={stats.active} />
          <Stat label="Locked" value={stats.locked} />
          <Stat label="Unconfirmed" value={stats.unconfirmed} />
          <Stat label="Deleted" value={stats.deleted} />
        </dl>
      )}

      <UserFilters filters={filters} roles={roles} />

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
                <TableHead>User</TableHead>
                <TableHead>Roles</TableHead>
                <TableHead>State</TableHead>
                <TableHead>Last sign-in (UTC)</TableHead>
                <TableHead>Created</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {page.items.map((user) => {
                const name = `${user.firstName} ${user.lastName}`.trim();
                return (
                  <TableRow key={user.id}>
                    <TableCell>
                      <Link href={`${USERS_PATH}/${user.id}`} className="block font-medium hover:underline">
                        {name || user.email}
                      </Link>
                      <span className="text-xs text-muted-foreground">{user.email}</span>
                    </TableCell>
                    <TableCell className="text-muted-foreground">{user.roles.join(", ") || "—"}</TableCell>
                    <TableCell className="whitespace-normal">
                      <UserStateBadges user={user} />
                    </TableCell>
                    <TableCell className="text-muted-foreground tabular-nums">{formatDateTime(user.lastLoginAt)}</TableCell>
                    <TableCell className="text-muted-foreground tabular-nums">{formatDate(user.createdAt)}</TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
          <Pager
            path={USERS_PATH}
            params={linkParams}
            pageNumber={page.pageNumber}
            totalPages={page.totalPages}
            totalCount={page.totalCount}
          />
        </>
      )}

      {page && page.items.length === 0 && (
        <div className="mt-12 text-center">
          <p className="font-medium">
            {page.totalCount > 0 ? "No results on this page" : hasActiveFilters(filters) ? "No users match these filters" : "No users yet"}
          </p>
          <Link href={page.totalCount > 0 ? buildAdminHref(USERS_PATH, linkParams) : USERS_PATH} className="mt-2 inline-block text-sm underline">
            {page.totalCount > 0 ? "Go to page 1" : "Clear filters"}
          </Link>
        </div>
      )}
    </div>
  );
}
