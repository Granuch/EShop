import { buildHref, getCategories, ShopParams } from "@/lib/temp.ts";
import Link from "next/link";

export default async function FilterSidebar({ params }: { params: ShopParams }) {
  const linkBase = "block rounded-md px-2 py-1.5 transition-colors";
  const linkActive = "bg-muted font-medium text-foreground";
  const linkIdle = "text-muted-foreground hover:text-foreground";
  const categories = await getCategories()

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
                href={buildHref({ ...params, category: undefined })}
                aria-current={!params.category ? "page" : undefined}
                className={`${linkBase} ${!params.category ? linkActive : linkIdle}`}
              >
                All products
              </Link>
            </li>
            {categories.map((c:any) => {
              const active = params.category === c.slug;
              return (
                <li key={c.slug}>
                  <Link
                    href={buildHref({ ...params, category: c.slug })}
                    aria-current={active ? "page" : undefined}
                    className={`${linkBase} ${active ? linkActive : linkIdle}`}
                  >
                    {c.label}
                  </Link>
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