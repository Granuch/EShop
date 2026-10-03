import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type { PagedResult } from "@/lib/admin/types/common";
import type { Category, Product, ProductDetails, ProductListQuery } from "@/lib/admin/types/catalog";

// Catalog's reads decide visibility by the Admin ROLE, not by a permission: without the role, drafts and deleted
// categories are silently left out (catalog.md "Who sees what"). Writes answer 403 without it.

/**
 * GET /api/v1/products. Counts against Catalog's `search` limit (30 / 60 s per client IP), which every user of this
 * app shares until T1, because all calls leave from the Next server.
 */
export async function listProducts(query: ProductListQuery): Promise<PagedResult<Product>> {
  await requirePermission("catalog.read");
  return adminFetch<PagedResult<Product>>(buildAdminHref("/api/v1/products", { ...query }));
}

/** GET /api/v1/products/{id}. A draft is 404 for a caller without the Admin role; a deleted product for everyone. */
export async function getProduct(id: string): Promise<ProductDetails> {
  await requirePermission("catalog.read");
  return adminFetch<ProductDetails>(`/api/v1/products/${encodeURIComponent(id)}`);
}

/** GET /api/v1/categories: the whole tree; with the Admin role it includes deleted categories (isActive: false). */
export async function getCategoryTree(): Promise<Category[]> {
  await requirePermission("catalog.read");
  return adminFetch<Category[]>("/api/v1/categories");
}

export interface CategoryOption {
  id: string;
  name: string;
  /** "Electronics / Phones". */
  path: string;
  depth: number;
  isActive: boolean;
}

/** Depth-first, in the tree's own order (displayOrder, then name), for pickers and id → name lookups. */
export function flattenCategories(tree: Category[]): CategoryOption[] {
  const out: CategoryOption[] = [];
  const walk = (nodes: Category[], depth: number, prefix: string) => {
    for (const node of nodes) {
      const path = prefix ? `${prefix} / ${node.name}` : node.name;
      out.push({ id: node.id, name: node.name, path, depth, isActive: node.isActive });
      walk(node.childCategories ?? [], depth + 1, path);
    }
  };
  walk(tree, 0, "");
  return out;
}
