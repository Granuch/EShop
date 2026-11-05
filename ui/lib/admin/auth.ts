import "server-only";

import { cache } from "react";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import type { UserProfile } from "@/lib/session";
import { adminFetch, ApiError } from "@/lib/admin/api";
import { isPermission } from "@/lib/admin/permissions";
import type { Permission } from "@/lib/admin/types/common";

const SIGN_IN = "/autorization";
/** Account-state codes the profile read answers for an account that can no longer be used (F-34). */
const ACCOUNT_GONE = ["Account.NotFound", "Account.UserNotFound", "Auth.AccountDisabled"];

/** The parts of the profile the admin uses. Only plain data: safe to pass to a client component piecewise. */
export interface AdminSession {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  permissions: Permission[];
}

export type AdminSessionResult =
  | { status: "ok"; session: AdminSession }
  | { status: "unavailable"; reason: string };

/**
 * Reads the caller's profile once per request. Redirects to sign-in when signed out (no token, or the profile
 * answers 401), and reports any other failure instead of throwing, so the admin layout can render it.
 * It does not use getSession(), which answers null for both "signed out" and "the gateway is down".
 */
export const loadAdminSession = cache(async (): Promise<AdminSessionResult> => {
  if (!(await cookies()).get("access_token")?.value) redirect(SIGN_IN);

  let profile: UserProfile;
  try {
    profile = await adminFetch<UserProfile>("/api/v1/account/profile");
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) redirect(SIGN_IN);
    // A deleted or deactivated account whose access token has not expired yet: retrying cannot help, so it is
    // treated as signed out (identity.md "Frontend notes", F-34: these codes and statuses vary by endpoint).
    if (error instanceof ApiError && ACCOUNT_GONE.includes(error.errorCode ?? "")) redirect(SIGN_IN);
    const reason = error instanceof ApiError
      ? `The account service answered ${error.status}${error.errorCode ? ` (${error.errorCode})` : ""}.`
      : "The API gateway could not be reached.";
    return { status: "unavailable", reason };
  }

  return {
    status: "ok",
    session: {
      id: profile.id,
      email: profile.email,
      firstName: profile.firstName,
      lastName: profile.lastName,
      roles: profile.roles ?? [],
      permissions: (profile.permissions ?? []).filter(isPermission),
    },
  };
});

/** The session, for pages and actions. Throws (to error.tsx) when the profile could not be read. */
export async function getAdminSession(): Promise<AdminSession> {
  const result = await loadAdminSession();
  if (result.status !== "ok") throw new Error(`Could not load your account. ${result.reason}`);
  return result.session;
}

export function hasPermission(session: AdminSession, permission: Permission): boolean {
  return session.permissions.includes(permission);
}

export function hasAnyPermission(session: AdminSession): boolean {
  return session.permissions.length > 0;
}

export class PermissionDeniedError extends Error {
  constructor(readonly permission: Permission) {
    super(`This account does not hold the ${permission} permission.`);
    this.name = "PermissionDeniedError";
  }
}

/**
 * The data-access check: every admin DAL function and server action calls it first. Pages check with
 * hasPermission() beforehand and render AccessDenied, so in a page this only fires as a second line of defence.
 */
export async function requirePermission(permission: Permission): Promise<AdminSession> {
  const session = await getAdminSession();
  if (!hasPermission(session, permission)) throw new PermissionDeniedError(permission);
  return session;
}
