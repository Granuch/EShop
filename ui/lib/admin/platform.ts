import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import type { SystemHealth } from "@/lib/admin/types/platform";

/** GET /api/v1/admin/health. Always 200 when allowed; can take about 5 s while a service hangs. */
export async function getSystemHealth(): Promise<SystemHealth> {
  await requirePermission("system.manage");
  return adminFetch<SystemHealth>("/api/v1/admin/health");
}
