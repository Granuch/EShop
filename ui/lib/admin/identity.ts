import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import type { AdminUserDetails } from "@/lib/admin/types/identity";

/** GET /api/v1/admin/users/{id}: includes deleted users; 404 User.NotFound for an unknown id. */
export async function getAdminUser(id: string): Promise<AdminUserDetails> {
  await requirePermission("users.read");
  return adminFetch<AdminUserDetails>(`/api/v1/admin/users/${encodeURIComponent(id)}`);
}
