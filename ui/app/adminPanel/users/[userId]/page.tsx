import Link from "next/link";
import { notFound } from "next/navigation";
import AccessDenied from "@/components/Admin/accessDenied";
import ActionButton from "@/components/Admin/actionButton";
import PageHeader from "@/components/Admin/pageHeader";
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
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { formatDateTime, formatMoney } from "@/lib/admin/format";
import { getAdminUser, getAdminUserRoles, getAdminUserSessions, listRoles } from "@/lib/admin/identity";
import { listUserOrders } from "@/lib/admin/ordering";
import { listUserPayments } from "@/lib/admin/payment";
import type { AdminUserDetails } from "@/lib/admin/types/identity";
import {
  accountAction,
  changeEmailAction,
  deleteUserAction,
  lockUserAction,
  setRolesAction,
  updateUserAction,
} from "../actions";
import { USERS_PATH } from "../filters";
import { DONE_NOTICES, isDoneNotice } from "../notices";
import { EmailForm, LockForm, ProfileForm, RolesForm } from "../userForms";
import UserStateBadges from "../userStateBadges";

export const metadata = { title: "User · Admin · EShop" };

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}

function unavailable(reason: unknown): string {
  const failure = classifyFailure(reason);
  if (failure?.kind === "forbidden") return "This account may not read it.";
  if (failure?.kind === "rateLimited" || failure?.kind === "invalid") return failure.message;
  return "It could not be loaded.";
}

/** lockoutEnd is a DateTimeOffset ("…+00:00", F-35): parsed, never compared as a string. */
function lockedUntil(user: AdminUserDetails): string | null {
  return user.isLockedOut && user.lockoutEnd ? formatDateTime(new Date(user.lockoutEnd)) : null;
}

export default async function UserPage({ params, searchParams }: PageProps<"/adminPanel/users/[userId]">) {
  const session = await getAdminSession();
  if (!hasPermission(session, "users.read")) return <AccessDenied />;

  const { userId } = await params;
  if (!/^[0-9A-Za-z-]{1,64}$/.test(userId)) notFound();
  const { invited, created, done } = await searchParams;

  let user: AdminUserDetails;
  try {
    user = await getAdminUser(userId);
  } catch (error) {
    const failure = classifyFailure(error);
    if (failure?.kind === "notFound") notFound();
    if (failure?.kind === "forbidden") return <AccessDenied />;
    throw error;
  }

  const canManage = hasPermission(session, "users.manage");
  const canSetRoles = hasPermission(session, "roles.manage");
  const canReadOrders = hasPermission(session, "orders.read");
  const canReadPayments = hasPermission(session, "payments.read");
  const [rolesResult, allRolesResult, sessionsResult, ordersResult, paymentsResult] = await Promise.allSettled([
    getAdminUserRoles(user.id),
    canSetRoles ? listRoles({ pageSize: 100 }) : Promise.reject(null),
    getAdminUserSessions(user.id),
    canReadOrders ? listUserOrders(user.id, { pageSize: 10 }) : Promise.reject(null),
    canReadPayments ? listUserPayments(user.id, { pageSize: 10 }) : Promise.reject(null),
  ]);
  const currentRoles = rolesResult.status === "fulfilled" ? rolesResult.value : user.roles;
  const allRoles = allRolesResult.status === "fulfilled" ? allRolesResult.value.items.map((role) => role.name) : null;
  const name = `${user.firstName} ${user.lastName}`.trim() || user.email || user.id;
  const live = !user.isDeleted;
  const until = lockedUntil(user);
  const action = (operation: Parameters<typeof accountAction>[1]) => accountAction.bind(null, user.id, operation);

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
            <BreadcrumbPage className="max-w-64 truncate">{name}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader title={name} description={user.email ?? undefined}>
        <UserStateBadges user={user} />
        {hasPermission(session, "notifications.read") && (
          <Link href={`/adminPanel/notifications?userId=${user.id}`} className="text-sm underline">
            Emails to this user
          </Link>
        )}
        {hasPermission(session, "baskets.read") && (
          <Link href={`/adminPanel/baskets/${user.id}`} className="text-sm underline">
            Basket
          </Link>
        )}
      </PageHeader>

      {isDoneNotice(done) && (
        <Alert role="status">
          <AlertDescription>{DONE_NOTICES[done]}</AlertDescription>
        </Alert>
      )}

      {(invited || created) && (
        <Alert role="status">
          <AlertDescription>
            {invited
              ? "Account created without a password: the user was emailed a link to set one."
              : "Account created with the password you gave."}
          </AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="min-w-0 space-y-6 lg:col-span-2">
          <Card>
            <CardHeader>
              <CardTitle>Details</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-4 sm:grid-cols-3">
                <Field label="Id">
                  <span className="font-mono text-xs">{user.id}</span>
                </Field>
                <Field label="Phone">{user.phoneNumber || "—"}</Field>
                <Field label="Roles">{currentRoles.join(", ") || "None"}</Field>
                <Field label="Created">{formatDateTime(user.createdAt)}</Field>
                <Field label="Last sign-in">{formatDateTime(user.lastLoginAt)}</Field>
                <Field label="Last sign-in IP">{user.lastLoginIp ?? "—"}</Field>
                <Field label="Email confirmed">{user.emailConfirmed ? "Yes" : "No"}</Field>
                <Field label="Two-factor">{user.twoFactorEnabled ? "On" : "Off"}</Field>
                <Field label="Failed sign-ins">{user.accessFailedCount}</Field>
                <Field label="Locked until">{until ?? "Not locked"}</Field>
                <Field label="External sign-in">
                  {[user.hasGoogleLogin && "Google", user.hasGitHubLogin && "GitHub"].filter(Boolean).join(", ") || "None"}
                </Field>
                {user.isDeleted && <Field label="Deleted">{formatDateTime(user.deletedAt)}</Field>}
              </dl>
            </CardContent>
          </Card>

          {live && canManage && (
            <Card>
              <CardHeader>
                <CardTitle>Profile</CardTitle>
              </CardHeader>
              <CardContent>
                <ProfileForm
                  action={updateUserAction.bind(null, user.id)}
                  initial={{
                    firstName: user.firstName,
                    lastName: user.lastName,
                    phoneNumber: user.phoneNumber ?? "",
                    profilePictureUrl: user.profilePictureUrl ?? "",
                  }}
                />
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle>Sessions</CardTitle>
            </CardHeader>
            <CardContent>
              {sessionsResult.status === "rejected" ? (
                <p role="alert" className="text-sm text-destructive">{`Sessions: ${unavailable(sessionsResult.reason)}`}</p>
              ) : sessionsResult.value.length === 0 ? (
                <p className="text-sm text-muted-foreground">Never signed in.</p>
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Started (UTC)</TableHead>
                      <TableHead>From</TableHead>
                      <TableHead>State</TableHead>
                      <TableHead>Expires</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {sessionsResult.value.map((s) => (
                      <TableRow key={s.id}>
                        <TableCell className="tabular-nums">{formatDateTime(s.createdAt)}</TableCell>
                        <TableCell className="font-mono text-xs">{s.createdByIp ?? "—"}</TableCell>
                        <TableCell>
                          {s.isActive ? (
                            <Badge variant="secondary">Active</Badge>
                          ) : s.revokedAt ? (
                            <span className="text-muted-foreground">{`Ended: ${s.revokeReason ?? "revoked"}`}</span>
                          ) : (
                            <span className="text-muted-foreground">Expired</span>
                          )}
                        </TableCell>
                        <TableCell className="text-muted-foreground tabular-nums">{formatDateTime(s.expiresAt)}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>

          {canReadOrders && (
            <Card>
              <CardHeader>
                <CardTitle>Orders</CardTitle>
              </CardHeader>
              <CardContent>
                {ordersResult.status === "rejected" ? (
                  <p role="alert" className="text-sm text-destructive">{`Orders: ${unavailable(ordersResult.reason)}`}</p>
                ) : ordersResult.value.items.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No orders.</p>
                ) : (
                  <>
                    <ul className="divide-y text-sm">
                      {ordersResult.value.items.map((order) => (
                        <li key={order.id} className="flex items-center justify-between gap-4 py-1.5">
                          <Link href={`/adminPanel/orders/${order.id}`} className="font-mono text-xs hover:underline">
                            {order.id.slice(0, 8)}
                          </Link>
                          <span className="text-xs text-muted-foreground">{formatDateTime(order.createdAt)}</span>
                          <span className="text-xs">{`${order.status} · ${formatMoney(order.totalPrice)}`}</span>
                        </li>
                      ))}
                    </ul>
                    <Link href={`/adminPanel/orders?search=${user.id}`} className="mt-3 inline-block text-sm underline">
                      {`All ${ordersResult.value.totalCount} in the orders list`}
                    </Link>
                  </>
                )}
              </CardContent>
            </Card>
          )}
        </div>

        <div className="min-w-0 space-y-6">
          {canReadPayments && (
            <Card>
              <CardHeader>
                <CardTitle>Payments</CardTitle>
              </CardHeader>
              <CardContent>
                {paymentsResult.status === "rejected" ? (
                  <p role="alert" className="text-sm text-destructive">{`Payments: ${unavailable(paymentsResult.reason)}`}</p>
                ) : paymentsResult.value.items.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No payments.</p>
                ) : (
                  <>
                    <ul className="divide-y text-sm">
                      {paymentsResult.value.items.map((payment) => (
                        <li key={payment.id} className="flex items-center justify-between gap-4 py-1.5">
                          <Link href={`/adminPanel/payments/${payment.id}`} className="font-mono text-xs hover:underline">
                            {payment.id.slice(0, 8)}
                          </Link>
                          <span className="text-xs">{`${payment.status} · ${formatMoney(payment.amount)}`}</span>
                        </li>
                      ))}
                    </ul>
                    <Link href={`/adminPanel/payments?userId=${user.id}`} className="mt-3 inline-block text-sm underline">
                      {`All ${paymentsResult.value.totalCount} in the payments list`}
                    </Link>
                  </>
                )}
              </CardContent>
            </Card>
          )}

          {canManage && (
            <Card>
              <CardHeader>
                <CardTitle>Account</CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                {live ? (
                  <>
                    <div className="flex flex-wrap gap-2">
                      {user.isActive ? (
                        <ActionButton
                          action={action("deactivate")}
                          align="start"
                          confirm={{
                            title: "Deactivate this account?",
                            description: "The user cannot sign in and every session is signed out. Activate it again at any time.",
                            confirmLabel: "Deactivate",
                          }}
                        >
                          Deactivate
                        </ActionButton>
                      ) : (
                        <ActionButton action={action("activate")} align="start">
                          Activate
                        </ActionButton>
                      )}
                      <ActionButton action={action("revoke-tokens")} align="start">
                        Sign out everywhere
                      </ActionButton>
                      <ActionButton action={action("reset-password")} align="start">
                        Email a password reset
                      </ActionButton>
                      {!user.emailConfirmed && (
                        <ActionButton action={action("confirm-email")} align="start">
                          Mark email confirmed
                        </ActionButton>
                      )}
                      {user.twoFactorEnabled && (
                        <ActionButton
                          action={action("disable-2fa")}
                          align="start"
                          confirm={{
                            title: "Turn off two-factor authentication?",
                            description: "For a user who lost their authenticator. Their secret is discarded; they can set 2FA up again.",
                            confirmLabel: "Turn off 2FA",
                          }}
                        >
                          Turn off 2FA
                        </ActionButton>
                      )}
                    </div>
                    <div className="border-t pt-4">
                      {until ? (
                        <div className="space-y-2">
                          <p className="text-sm">{`Locked until ${until}.`}</p>
                          <ActionButton action={action("unlock")} align="start">
                            Unlock
                          </ActionButton>
                        </div>
                      ) : (
                        <LockForm action={lockUserAction.bind(null, user.id)} />
                      )}
                    </div>
                    <div className="border-t pt-4">
                      <ActionButton
                        action={deleteUserAction.bind(null, user.id)}
                        variant="destructive"
                        align="start"
                        confirm={{
                          title: "Delete this account?",
                          description: `${user.email ?? name} is deactivated and signed out, and leaves the user list. The email stays taken. It can be restored.`,
                          confirmLabel: "Delete account",
                        }}
                      >
                        Delete account
                      </ActionButton>
                    </div>
                  </>
                ) : (
                  <ActionButton action={action("restore")} variant="default" align="start">
                    Restore account
                  </ActionButton>
                )}
              </CardContent>
            </Card>
          )}

          {live && canManage && user.email && (
            <Card>
              <CardHeader>
                <CardTitle>Email</CardTitle>
              </CardHeader>
              <CardContent>
                <EmailForm action={changeEmailAction.bind(null, user.id)} email={user.email} />
              </CardContent>
            </Card>
          )}

          {live && canSetRoles && allRoles && (
            <Card>
              <CardHeader>
                <CardTitle>Roles</CardTitle>
              </CardHeader>
              <CardContent>
                <RolesForm action={setRolesAction.bind(null, user.id)} roles={allRoles} current={currentRoles} />
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}
