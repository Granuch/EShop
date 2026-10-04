// Copied from docs/01-overview/frontend/identity.md (TypeScript, admin part). Keep in step with that file.

export type AdminUserSortBy = 'CreatedAt' | 'Email' | 'LastLoginAt';

export interface AdminUserListQuery {
  search?: string;
  role?: string;
  isActive?: boolean;
  /** true lists ONLY deleted users; false or omitted lists only live ones. */
  isDeleted?: boolean;
  emailConfirmed?: boolean;
  twoFactorEnabled?: boolean;
  /** ISO-8601; a date without a zone is read as UTC. */
  createdFrom?: string;
  createdTo?: string;
  lastLoginFrom?: string;
  lastLoginTo?: string;
  sortBy?: AdminUserSortBy;
  isDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export interface AdminUserStatsQuery {
  from?: string;
  to?: string;
}

export interface AdminUser {
  id: string;
  email: string | null;
  userName: string | null;
  firstName: string;
  lastName: string;
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  isActive: boolean;
  isDeleted: boolean;
  deletedAt: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  /** DateTimeOffset: "2026-09-23T18:59:25+00:00", not Z. */
  lockoutEnd: string | null;
  isLockedOut: boolean;
  roles: string[];
}

export interface AdminUserDetails extends AdminUser {
  phoneNumber: string | null;
  profilePictureUrl: string | null;
  phoneNumberConfirmed: boolean;
  lastLoginIp: string | null;
  lockoutEnabled: boolean;
  accessFailedCount: number;
  hasGoogleLogin: boolean;
  hasGitHubLogin: boolean;
}

export interface AdminUserStats {
  total: number;
  newInPeriod: number;
  active: number;
  locked: number;
  unconfirmed: number;
  deleted: number;
}

export interface AdminUserSession {
  id: string;
  createdAt: string;
  expiresAt: string;
  createdByIp: string | null;
  revokedAt: string | null;
  revokedByIp: string | null;
  revokeReason: string | null;
  isActive: boolean;
}

export interface CreateUserRequest {
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string | null;
  /** Omit to create the account without a password and email a set-password link. */
  password?: string | null;
  /** Omit for ["User"]; [] creates a user with no role. At most 10. */
  roles?: string[] | null;
  emailConfirmed?: boolean;
}

export interface CreateUserResponse {
  userId: string;
  email: string;
  /** true when no password was given and a set-password email was queued. */
  inviteSent: boolean;
}

/** Every field optional: omitted or null leaves it unchanged; "" clears phoneNumber / profilePictureUrl. */
export interface UpdateUserRequest {
  firstName?: string | null;
  lastName?: string | null;
  phoneNumber?: string | null;
  profilePictureUrl?: string | null;
}

export interface ChangeUserEmailRequest {
  email: string;
  /** Omitted means false: the new address becomes unconfirmed. */
  markConfirmed?: boolean;
}

export interface LockUserRequest {
  /** Required, in the future. ISO-8601 with Z or an offset. */
  until: string;
  reason?: string | null;
}

export interface SetUserRolesRequest {
  /** The complete new set. Omitted or [] removes every role. */
  roles?: string[] | null;
}

// ---- Admin: roles ----

/** GET /roles and GET /roles/{roleName}/users. Both answer PagedResult<Role> / PagedResult<UserInRole>. */
export interface RoleListQuery {
  pageNumber?: number;
  /** 1-100; default 50. */
  pageSize?: number;
}

export interface Role {
  id: string;
  name: string;
  description: string | null;
}

export interface CreateRoleRequest {
  name: string;
  description?: string | null;
}

export interface UpdateRoleRequest {
  /** Replaces the description; omitted or null clears it. */
  description?: string | null;
}

export interface UserInRole {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
}
