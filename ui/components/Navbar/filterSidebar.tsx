import { buildHref, findCategoryBySlug, findCategoryPath, getCategoryTree, getProductCount, ShopParams } from "@/lib/temp";
import Link from "next/link";

export default async function FilterSidebar({ params }: { params: ShopParams }) {
  const linkBase = "block rounded-md px-2 py-1.5 transition-colors";
  const linkActive = "bg-muted font-medium text-foreground";
  const linkIdle = "text-muted-foreground hover:text-foreground";
  const tree = await getCategoryTree()
  // The selected category's root expands into its subcategories; the others stay collapsed.
  const selected = params.category ? findCategoryBySlug(tree, params.category) : undefined
  const openRootId = selected ? findCategoryPath(tree, selected.id)[0]?.id : undefined
  const openRoot = tree.find((c) => c.id === openRootId)
  const counted = [...tree, ...(openRoot?.childCategories ?? [])]
  const counts = new Map(await Promise.all(counted.map(async (c) => [c.id, await getProductCount(c.id)] as const)))
  const count = (id: string) => {
    const n = counts.get(id)
    return n === null || n === undefined ? null : <span className="ml-auto pl-2 text-xs tabular-nums text-muted-foreground">{n}</span>
  }

  return (
    <aside className="hidden w-56 shrink-0 lg:block">
      <div className="sticky top-32 space-y-8">
        <section aria-labelledby="filter-category">
          <h2 id="filter-category" className="text-sm font-semibold">
            Category
          </h2>
          <ul className="mt-3 space-y-0.5 text-sm">
            <li>
              <Link
                href={buildHref({ ...params, category: undefined, page: undefined })}
                aria-current={!params.category ? "page" : undefined}
                className={`${linkBase} ${!params.category ? linkActive : linkIdle}`}
              >
                All products
              </Link>
            </li>
            {tree.map((c) => {
              const active = params.category === c.slug;
              const open = c.id === openRootId && c.childCategories.length > 0;
              return (
                <li key={c.slug}>
                  <Link
                    href={buildHref({ ...params, category: c.slug, page: undefined })}
                    aria-current={active ? "page" : undefined}
                    aria-expanded={c.childCategories.length > 0 ? open : undefined}
                    className={`${linkBase} flex items-center ${active ? linkActive : linkIdle}`}
                  >
                    {c.name}
                    {count(c.id)}
                  </Link>
                  {open && (
                    <ul className="my-1 ml-3 space-y-0.5 border-l pl-2">
                      {c.childCategories.map((child) => {
                        const childActive = params.category === child.slug;
                        return (
                          <li key={child.slug}>
                            <Link
                              href={buildHref({ ...params, category: child.slug, page: undefined })}
                              aria-current={childActive ? "page" : undefined}
                              className={`${linkBase} flex items-center ${childActive ? linkActive : linkIdle}`}
                            >
                              {child.name}
                              {count(child.id)}
                            </Link>
                          </li>
                        );
                      })}
                    </ul>
                  )}
                </li>
              );
            })}
          </ul>
        </section>

        {/* Plain GET form: works without client-side JS */}
        <form action="/" aria-labelledby="filter-price" className="space-y-3">
          <h2 id="filter-price" className="text-sm font-semibold">
            Price
          </h2>
          {params.category && (
            <input type="hidden" name="category" value={params.category} />
          )}
          {params.sort && <input type="hidden" name="sort" value={params.sort} />}

          <div className="flex items-center gap-2">
            <input
              name="minPrice"
              type="number"
              min={0}
              defaultValue={params.minPrice}
              placeholder="Min"
              aria-label="Minimum price"
              className="h-9 w-full rounded-md border bg-background px-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
            <span aria-hidden className="text-muted-foreground">
              –
            </span>
            <input
              name="maxPrice"
              type="number"
              min={0}
              defaultValue={params.maxPrice}
              placeholder="Max"
              aria-label="Maximum price"
              className="h-9 w-full rounded-md border bg-background px-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
          </div>
          <button
            type="submit"
            className="h-9 w-full rounded-md border text-sm font-medium transition-colors hover:bg-muted"
          >
            Apply price
          </button>
        </form>
      </div>
    </aside>
  );
}