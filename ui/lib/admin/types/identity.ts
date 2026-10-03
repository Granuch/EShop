// Copied from docs/01-overview/frontend/identity.md (TypeScript). Only what the admin uses so far; the rest of the
// admin users surface arrives with P8.

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
