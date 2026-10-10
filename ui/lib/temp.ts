import { cache } from "react";
import { gatewayFetch } from "./api";

export type ShopParams = {
  category?: string;
  sort?: string;
  minPrice?: string;
  maxPrice?: string;
  page?: string;
};

export type CategoryNode = {
  id: string;
  name: string;
  slug: string;
  childCategories: CategoryNode[];
};

// The Navbar and the page both need the tree, so one request reaches the gateway once (its limit is shared).
export const getCategoryTree = cache(async (): Promise<CategoryNode[]> => {
    const res = await gatewayFetch("/api/v1/categories")
    if (!res.ok) return []
    return await res.json()
})

/** The root categories only, as the Navbar and the filter sidebar list them. */
export async function getCategories() {
    const tree = await getCategoryTree()

    return tree.map((item) => ({
        id: item.id,
        slug: item.slug,
        label: item.name
    }))
}

/** Any category in the tree, at any depth. */
export function findCategoryBySlug(tree: CategoryNode[], slug: string): CategoryNode | undefined {
  for (const node of tree) {
    if (node.slug === slug) return node;
    const found = findCategoryBySlug(node.childCategories ?? [], slug);
    if (found) return found;
  }
  return undefined;
}

/**
 * How many published products a category holds, its subcategories included (Catalog's CategoryId filter covers the
 * subtree). One cached request per category, shared by every visitor for a minute, so it carries no one's address.
 */
export const getProductCount = cache(async (categoryId: string): Promise<number | null> => {
    const query = new URLSearchParams({ CategoryId: categoryId, PageSize: "1" })
    const res = await gatewayFetch(`/api/v1/products?${query}`, { next: { revalidate: 60 } })
    if (!res.ok) return null
    const data = await res.json() as { totalCount?: number }
    return data.totalCount ?? null
})

/** The chain from a root category down to the one with this id, or [] when it is not in the tree. */
export function findCategoryPath(tree: CategoryNode[], id: string): CategoryNode[] {
  for (const node of tree) {
    if (node.id === id) return [node];
    const path = findCategoryPath(node.childCategories ?? [], id);
    if (path.length > 0) return [node, ...path];
  }
  return [];
}

export const sortOptions = [
  { value: "newest", label: "Newest" },
  { value: "price_asc", label: "Price: low to high" },
  { value: "price_desc", label: "Price: high to low" },
];

export function toApiSort(sort?:string) {
  switch (sort) {
    case "price_asc": return {SortBy: "Price", IsDescending: "false"};
    case "price_desc": return {SortBy: "Price", IsDescending: "true"};
    default:            return { SortBy: "CreatedAt", IsDescending: "true" }; 
  }
}

/** Builds a "/?category=...&sort=..." link, skipping empty values. */
export function buildHref(params: ShopParams) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) query.set(key, value);
  }
  const qs = query.toString();
  return qs ? `/?${qs}` : "/";
}