"use server";

import { revalidatePath } from "next/cache";
import { toFormState } from "@/lib/admin/actionErrors";
import { ApiError } from "@/lib/admin/api";
import type { FormState } from "@/lib/admin/forms";
import { invalidateCatalogCache } from "@/lib/admin/platform";
import type { CacheFamily } from "@/lib/admin/types/platform";

const FAMILIES: CacheFamily[] = ["products:list", "categories:list"];

/**
 * POST /admin/cache/invalidate. "" bumps both families. The bump is the whole request, so a 503 Cache.Unavailable is
 * real (its detail names which families went through before the failure).
 */
export async function invalidateCacheAction(_state: FormState, formData: FormData): Promise<FormState> {
  const raw = String(formData.get("family") ?? "");
  const family = (FAMILIES as string[]).includes(raw) ? (raw as CacheFamily) : undefined;
  try {
    const report = await invalidateCatalogCache(family);
    // The storefront's own fetch cache holds the product list for 60 s; refresh it too.
    revalidatePath("/", "layout");
    return { status: "ok", message: `Bumped: ${report.families.join(", ")}. Old entries stop being read and lapse on their own.` };
  } catch (error) {
    const state = toFormState(error, { fields: [] });
    return error instanceof ApiError && error.status === 503 && error.problem?.detail
      ? { ...state, message: error.problem.detail }
      : state;
  }
}
