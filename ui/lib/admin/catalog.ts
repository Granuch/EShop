import "server-only";

import { adminFetch } from "@/lib/admin/api";
import { requirePermission } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import type { PagedResult } from "@/lib/admin/types/common";
import type {
  AdjustStockRequest,
  Category,
  CreatedResourceResponse,
  CreateProductRequest,
  Product,
  ProductDetails,
  ProductListQuery,
  ProductStockResponse,
  UpdateProductRequest,
} from "@/lib/admin/types/catalog";

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

// ---- Writes: every one needs catalog.write here and the Admin role at the service ----

function jsonBody(body: unknown): RequestInit {
  return { body: JSON.stringify(body), headers: { "Content-Type": "application/json" } };
}

/** POST /api/v1/products. Creates a Draft. Send exactly the documented fields: an unknown one is 400 (F-02). */
export async function createProduct(body: CreateProductRequest): Promise<CreatedResourceResponse> {
  await requirePermission("catalog.write");
  return adminFetch<CreatedResourceResponse>("/api/v1/products", { method: "POST", ...jsonBody(body) });
}

/** PUT /api/v1/products/{id}. `stockQuantity` replaces the stock, so callers send the value they just re-read. */
export async function updateProduct(id: string, body: UpdateProductRequest): Promise<void> {
  await requirePermission("catalog.write");
  await adminFetch<void>(`/api/v1/products/${encodeURIComponent(id)}`, { method: "PUT", ...jsonBody(body) });
}

/** PATCH /api/v1/products/{id}/stock: exactly one of delta / absolute. */
export async function adjustStock(id: string, body: AdjustStockRequest): Promise<ProductStockResponse> {
  await requirePermission("catalog.write");
  return adminFetch<ProductStockResponse>(`/api/v1/products/${encodeURIComponent(id)}/stock`, {
    method: "PATCH",
    ...jsonBody(body),
  });
}

/** POST /api/v1/products/{id}/publish | unpublish. Idempotent, no body. */
export async function setPublished(id: string, published: boolean): Promise<void> {
  await requirePermission("catalog.write");
  await adminFetch<void>(`/api/v1/products/${encodeURIComponent(id)}/${published ? "publish" : "unpublish"}`, {
    method: "POST",
  });
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

/**
 * Choices for a product's category: live categories only (a deleted one is 400 Category.NotFound), labelled by path.
 * `keepId` keeps the product's current category even if it was deleted since, so the form does not silently change it.
 */
export function toCategoryChoices(options: CategoryOption[], keepId?: string): { id: string; label: string }[] {
  return options
    .filter((option) => option.isActive || option.id === keepId)
    .map((option) => ({ id: option.id, label: option.isActive ? option.path : `${option.path} (deleted)` }));
}
