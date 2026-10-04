import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type {
  AuditLogPage,
  AuditLogQuery,
  CacheFamily,
  CacheInvalidationReport,
  FeatureFlags,
  SystemHealth,
  SystemSettings,
} from "@/lib/admin/types/platform";

// The gateway serves these four itself, asking every service at once with 5 s each; a service that does not answer
// never fails the page, it is named (unavailableServices / reachable: false). Expect up to about 5 s.

/** GET /api/v1/admin/health. Always 200 when allowed. */
export async function getSystemHealth(): Promise<SystemHealth> {
  await requirePermission("system.manage");
  return adminFetch<SystemHealth>("/api/v1/admin/health");
}

/** GET /api/v1/admin/audit: newest first across five services; keep every filter while paging by cursor (F-57). */
export async function getAuditLog(query: AuditLogQuery): Promise<AuditLogPage> {
  await requirePermission("audit.read");
  return adminFetch<AuditLogPage>(buildAdminHref("/api/v1/admin/audit", { ...query }));
}

/** GET /api/v1/admin/settings: pricing (Ordering) and payment provider (Payment), read-only. */
export async function getSystemSettings(): Promise<SystemSettings> {
  await requirePermission("system.manage");
  return adminFetch<SystemSettings>("/api/v1/admin/settings");
}

/** GET /api/v1/admin/feature-flags: the gateway's fault injection and Payment's simulator, read-only. */
export async function getFeatureFlags(): Promise<FeatureFlags> {
  await requirePermission("system.manage");
  return adminFetch<FeatureFlags>("/api/v1/admin/feature-flags");
}

/**
 * POST /api/v1/admin/cache/invalidate?family=: bumps one of Catalog's list families, or both when omitted. Nothing is
 * deleted; entries lapse on their TTL. 503 Cache.Unavailable when Redis refused a bump.
 */
export async function invalidateCatalogCache(family?: CacheFamily): Promise<CacheInvalidationReport> {
  await requirePermission("system.manage");
  return adminFetch<CacheInvalidationReport>(buildAdminHref("/api/v1/admin/cache/invalidate", { family }), { method: "POST" });
}
