import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type {
  AbandonedBasketsQuery,
  AdminBasketPageDto,
  Basket,
  BasketScanQuery,
  OutboxDeadLetterCount,
  OutboxDeadLetterPageDto,
  OutboxDeadLettersQuery,
  OutboxReplayResult,
} from "@/lib/admin/types/basket";

// Basket's admin surface (basket.md "Admin panel"): carts and abandoned take baskets.read; the dead-letter details
// system.manage; the count and the replay the Admin ROLE (F-09), as does reading one user's basket.

/** GET /admin/carts: a Redis scan; a page can be short or empty while nextCursor is still set. */
export async function listBaskets(query: BasketScanQuery): Promise<AdminBasketPageDto> {
  await requirePermission("baskets.read");
  return adminFetch<AdminBasketPageDto>(buildAdminHref("/api/v1/basket/admin/carts", { ...query }));
}

/** GET /admin/abandoned: not changed for `olderThan` ("90m", "24h", "3d"; at most 30 days). */
export async function listAbandonedBaskets(query: AbandonedBasketsQuery): Promise<AdminBasketPageDto> {
  await requirePermission("baskets.read");
  return adminFetch<AdminBasketPageDto>(buildAdminHref("/api/v1/basket/admin/abandoned", { ...query }));
}

/** GET /api/v1/basket/{userId}: any user's basket for the Admin role (read only); an empty one, never a 404. */
export async function getUserBasket(userId: string): Promise<Basket> {
  await requirePermission("baskets.read");
  return adminFetch<Basket>(`/api/v1/basket/${encodeURIComponent(userId)}`);
}

/** GET /admin/outbox/dead-letters/details: newest first, offset-paged. */
export async function listDeadLetters(query: OutboxDeadLettersQuery): Promise<OutboxDeadLetterPageDto> {
  await requirePermission("system.manage");
  return adminFetch<OutboxDeadLetterPageDto>(buildAdminHref("/api/v1/basket/admin/outbox/dead-letters/details", { ...query }));
}

/** GET /admin/outbox/dead-letters: the count only (Admin role). */
export async function countDeadLetters(): Promise<OutboxDeadLetterCount> {
  await requirePermission("system.manage");
  return adminFetch<OutboxDeadLetterCount>("/api/v1/basket/admin/outbox/dead-letters");
}

/** POST /admin/outbox/dead-letters/replay: at most 1000 per call, oldest first (Admin role). */
export async function replayDeadLetters(): Promise<OutboxReplayResult> {
  await requirePermission("system.manage");
  return adminFetch<OutboxReplayResult>("/api/v1/basket/admin/outbox/dead-letters/replay", { method: "POST" });
}
