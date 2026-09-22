export type ShopParams = {
  category?: string;
  sort?: string;
  minPrice?: string;
  maxPrice?: string;
};

export async function getCategories() {
    const res = await fetch("http://localhost:7000/api/v1/categories")
    const data = await res.json()

    const categories = data.map((item:any) => ({
        slug: item.slug,
        label: item.name
    }))

    return categories
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