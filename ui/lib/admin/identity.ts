import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type { PagedResult } from "@/lib/admin/types/common";
import type {
  AdminUser,
  AdminUserDetails,
  AdminUserListQuery,
  AdminUserSession,
  AdminUserStats,
  AdminUserStatsQuery,
  ChangeUserEmailRequest,
  CreateRoleRequest,
  CreateUserRequest,
  CreateUserResponse,
  LockUserRequest,
  Role,
  RoleListQuery,
  SetUserRolesRequest,
  UpdateRoleRequest,
  UpdateUserRequest,
  UserInRole,
} from "@/lib/admin/types/identity";

// Identity's admin users surface checks permissions (users.read; writes users.read + users.manage; the role set
// roles.manage). The roles endpoints still need the Admin ROLE (identity.md "Roles").

const user = (id: string, rest = "") => `/api/v1/admin/users/${encodeURIComponent(id)}${rest}`;

function jsonBody(body: unknown): RequestInit {
  return { body: JSON.stringify(body), headers: { "Content-Type": "application/json" } };
}

// ---- Reads ----

/** GET /api/v1/admin/users: live users unless isDeleted=true (then only deleted ones). */
export async function listAdminUsers(query: AdminUserListQuery): Promise<PagedResult<AdminUser>> {
  await requirePermission("users.read");
  return adminFetch<PagedResult<AdminUser>>(buildAdminHref("/api/v1/admin/users", { ...query }));
}

/** GET /stats: `from`/`to` bound only newInPeriod; the rest cover every user. */
export async function getAdminUserStats(query: AdminUserStatsQuery): Promise<AdminUserStats> {
  await requirePermission("users.read");
  return adminFetch<AdminUserStats>(buildAdminHref("/api/v1/admin/users/stats", { ...query }));
}

/** GET /api/v1/admin/users/{id}: includes deleted users; 404 User.NotFound for an unknown id. */
export async function getAdminUser(id: string): Promise<AdminUserDetails> {
  await requirePermission("users.read");
  return adminFetch<AdminUserDetails>(user(id));
}

/** GET /{id}/roles: the role names, also for a deleted user. */
export async function getAdminUserRoles(id: string): Promise<string[]> {
  await requirePermission("users.read");
  return adminFetch<string[]>(user(id, "/roles"));
}

/** GET /{id}/sessions: newest first, unpaged, revoked and expired ones included. */
export async function getAdminUserSessions(id: string): Promise<AdminUserSession[]> {
  await requirePermission("users.read");
  return adminFetch<AdminUserSession[]>(user(id, "/sessions"));
}

// ---- Writes (users.manage) ----

/** POST /api/v1/admin/users. No password → an invite email; omitted roles → ["User"]. Read the id from the body (F-32). */
export async function createAdminUser(body: CreateUserRequest): Promise<CreateUserResponse> {
  await requirePermission("users.manage");
  return adminFetch<CreateUserResponse>("/api/v1/admin/users", { method: "POST", ...jsonBody(body) });
}

/** PUT /{id}: every field optional; "" clears phoneNumber / profilePictureUrl. */
export async function updateAdminUser(id: string, body: UpdateUserRequest): Promise<void> {
  await requirePermission("users.manage");
  await adminFetch<void>(user(id), { method: "PUT", ...jsonBody(body) });
}

/** PUT /{id}/email: omitting markConfirmed unconfirms the address, which signs the user out everywhere. */
export async function changeAdminUserEmail(id: string, body: ChangeUserEmailRequest): Promise<void> {
  await requirePermission("users.manage");
  await adminFetch<void>(user(id, "/email"), { method: "PUT", ...jsonBody(body) });
}

export type AccountOperation =
  | "activate"
  | "deactivate"
  | "restore"
  | "unlock"
  | "reset-password"
  | "confirm-email"
  | "disable-2fa"
  | "revoke-tokens";

/** The body-less POST /{id}/{operation} writes; each answers 204. */
export async function accountOperation(id: string, operation: AccountOperation): Promise<void> {
  await requirePermission("users.manage");
  await adminFetch<void>(user(id, `/${operation}`), { method: "POST" });
}

/** POST /{id}/lock: `until` must be in the future, with Z or an offset. Sessions are not revoked. */
export async function lockAdminUser(id: string, body: LockUserRequest): Promise<void> {
  await requirePermission("users.manage");
  await adminFetch<void>(user(id, "/lock"), { method: "POST", ...jsonBody(body) });
}

/** DELETE /{id}: soft delete (also deactivates and signs out); the email stays taken. */
export async function deleteAdminUser(id: string): Promise<void> {
  await requirePermission("users.manage");
  await adminFetch<void>(user(id), { method: "DELETE" });
}

/** PUT /{id}/roles: replaces the whole set (roles.manage); [] removes every role; unknown names change nothing. */
export async function setAdminUserRoles(id: string, body: SetUserRolesRequest): Promise<void> {
  await requirePermission("roles.manage");
  await adminFetch<void>(user(id, "/roles"), { method: "PUT", ...jsonBody(body) });
}

// ---- Roles (/api/v1/roles): the Admin ROLE at Identity; gated here by roles.manage for writes ----

const role = (id: string) => `/api/v1/roles/${encodeURIComponent(id)}`;
const roleUsers = (roleName: string, rest = "") => `/api/v1/roles/${encodeURIComponent(roleName)}/users${rest}`;

/** GET /api/v1/roles: by name; the default page of 50 normally holds every role. */
export async function listRoles(query: RoleListQuery = {}): Promise<PagedResult<Role>> {
  await requirePermission("users.read");
  return adminFetch<PagedResult<Role>>(buildAdminHref("/api/v1/roles", { ...query }));
}

/** GET /api/v1/roles/{id}: by id (a GUID), not by name. */
export async function getRole(id: string): Promise<Role> {
  await requirePermission("users.read");
  return adminFetch<Role>(role(id));
}

/** POST /api/v1/roles: a new role grants no permission (only Admin has a bundle). */
export async function createRole(body: CreateRoleRequest): Promise<Role> {
  await requirePermission("roles.manage");
  return adminFetch<Role>("/api/v1/roles", { method: "POST", ...jsonBody(body) });
}

/** PUT /api/v1/roles/{id}: replaces the description; the name cannot change. */
export async function updateRole(id: string, body: UpdateRoleRequest): Promise<void> {
  await requirePermission("roles.manage");
  await adminFetch<void>(role(id), { method: "PUT", ...jsonBody(body) });
}

/** DELETE /api/v1/roles/{id}: Admin and User cannot be deleted; members lose the role. */
export async function deleteRole(id: string): Promise<void> {
  await requirePermission("roles.manage");
  await adminFetch<void>(role(id), { method: "DELETE" });
}

/** GET /api/v1/roles/{roleName}/users: by email. */
export async function listRoleUsers(roleName: string, query: RoleListQuery = {}): Promise<PagedResult<UserInRole>> {
  await requirePermission("users.read");
  return adminFetch<PagedResult<UserInRole>>(buildAdminHref(roleUsers(roleName), { ...query }));
}

/** POST /api/v1/roles/{roleName}/users/{userId}: 400 Role.AddUserFailed if the user already has it. */
export async function addRoleUser(roleName: string, userId: string): Promise<void> {
  await requirePermission("roles.manage");
  await adminFetch<void>(roleUsers(roleName, `/${encodeURIComponent(userId)}`), { method: "POST" });
}

/** DELETE /api/v1/roles/{roleName}/users/{userId}: 400 Role.RemoveUserFailed if the user does not have it. */
export async function removeRoleUser(roleName: string, userId: string): Promise<void> {
  await requirePermission("roles.manage");
  await adminFetch<void>(roleUsers(roleName, `/${encodeURIComponent(userId)}`), { method: "DELETE" });
}
