import Image from "next/image";
import Item from "@/components/Item/item";
import { itemData } from "@/components/Item/types/itemType";
import Link from "next/link";
import { buildHref, findCategoryBySlug, findCategoryPath, getCategoryTree, getProductCount, sortOptions, toApiSort } from "@/lib/temp";
import FilterSidebar from "@/components/Navbar/filterSidebar";
import { gatewayFetch } from "@/lib/api";

type ShopParams = {
  category?: string;
  sort?: string;
  minPrice?: string;
  maxPrice?: string;
  page?: string;
};

// Divides evenly into the 2-, 3- and 4-column grids.
const PAGE_SIZE = 24

type ProductPage = { items: itemData[]; totalCount: number; totalPages: number; pageNumber: number }

async function fetchProducts(params: ShopParams): Promise<ProductPage> {
  const apiSort = toApiSort(params.sort)
  const page = Math.max(1, Number.parseInt(params.page ?? "1", 10) || 1)
  const query = new URLSearchParams({
    ...(params.category ? { CategoryId: params.category } : {}),
    ...(params.minPrice ? { MinPrice: params.minPrice } : {}),
    ...(params.maxPrice ? { MaxPrice: params.maxPrice } : {}),
    ...apiSort,
    PageNumber: String(page),
    PageSize: String(PAGE_SIZE),
  })
  const res = await gatewayFetch(`/api/v1/products?${query}`, {next: {revalidate: 60}})

  if(!res.ok) {
    console.error("Products fetch failed:", res.status, await res.text());
    return { items: [], totalCount: 0, totalPages: 0, pageNumber: page };
  }
  
  const data = await res.json()
  return { items: data.items, totalCount: data.totalCount, totalPages: data.totalPages, pageNumber: data.pageNumber ?? page }
}

// Page numbers to show: the first, the last, and two either side of the current one, with gaps as null.
function pageWindow(current: number, total: number): (number | null)[] {
  const pages: (number | null)[] = []
  for (let p = 1; p <= total; p++) {
    if (p === 1 || p === total || Math.abs(p - current) <= 2) pages.push(p)
    else if (pages[pages.length - 1] !== null) pages.push(null)
  }
  return pages
}


export default async function Home({
  searchParams,
}: {
  searchParams: Promise<ShopParams>;
}) {
  const params = await searchParams;
  // Search the whole tree: a product page's breadcrumbs link to subcategories, which the Navbar does not list.
  const tree = await getCategoryTree()
  const selectedCategory = params.category ? findCategoryBySlug(tree, params.category) : undefined

  const { items: products, totalCount, totalPages, pageNumber } = await fetchProducts({
    ...params,
    category: selectedCategory?.id
  })

  // A category page shows where it sits (All products › Electronics › Phones) and its root's subcategories as chips,
  // which is also how a phone, without the filter sidebar, reaches a subcategory.
  const categoryPath = selectedCategory ? findCategoryPath(tree, selectedCategory.id) : []
  const sectionRoot = categoryPath[0]
  const chipCategories = sectionRoot?.childCategories ?? []
  const chipCounts = await Promise.all(chipCategories.map((c) => getProductCount(c.id)))

  const activeSort = params.sort ?? "newest";
  const title = selectedCategory?.name ?? "All products";
  const isFiltered = Boolean(params.category || params.minPrice || params.maxPrice);
 
  return (
    <div className="mx-auto max-w-7xl px-4 pb-20 sm:px-6 lg:px-8">
      {/* Hero: only on the unfiltered home page */}
      {!isFiltered && (
        <section className="mt-6 rounded-2xl bg-primary p-8 text-primary-foreground sm:p-12">
          <h1 className="max-w-lg text-4xl font-semibold tracking-tight sm:text-5xl">
            New arrivals
          </h1>
          <p className="mt-3 max-w-md text-primary-foreground/80">
            Fresh products added this week.
          </p>
          <Link
            href={`${buildHref({ sort: "newest" })}#products`}
            className="mt-8 inline-flex h-11 items-center rounded-full bg-background px-6 text-sm font-medium text-foreground transition-opacity hover:opacity-90"
          >
            Shop new arrivals
          </Link>
        </section>
      )}
 
      <div className="mt-10 flex gap-10">
        <FilterSidebar params={params} />
 
        <section id="products" className="min-w-0 flex-1 scroll-mt-32">
          {categoryPath.length > 0 && (
            <nav aria-label="Breadcrumb" className="mb-3 flex flex-wrap items-center gap-1.5 text-sm text-muted-foreground">
              <Link href={buildHref({ sort: params.sort })} className="hover:text-foreground">All products</Link>
              {categoryPath.map((c, i) => (
                <span key={c.id} className="flex items-center gap-1.5">
                  <span aria-hidden>›</span>
                  {i === categoryPath.length - 1
                    ? <span aria-current="page" className="text-foreground">{c.name}</span>
                    : <Link href={buildHref({ category: c.slug, sort: params.sort })} className="hover:text-foreground">{c.name}</Link>}
                </span>
              ))}
            </nav>
          )}
          <div className="flex flex-wrap items-end justify-between gap-4">
            <div>
              <h2 className="text-2xl font-semibold tracking-tight">{title}</h2>
              <p className="mt-1 text-sm text-muted-foreground">
                {totalCount} {totalCount === 1 ? "product" : "products"}
              </p>
            </div>
 
            <nav aria-label="Sort products" className="flex flex-wrap gap-1 text-sm">
              {sortOptions.map((o) => {
                const active = activeSort === o.value;
                return (
                  <Link
                    key={o.value}
                    href={`${buildHref({ ...params, sort: o.value, page: undefined })}#products`}
                    aria-current={active ? "true" : undefined}
                    className={`rounded-full px-3 py-1.5 transition-colors ${
                      active
                        ? "bg-primary text-primary-foreground"
                        : "text-muted-foreground hover:bg-muted hover:text-foreground"
                    }`}
                  >
                    {o.label}
                  </Link>
                );
              })}
            </nav>
          </div>
 
          {sectionRoot && chipCategories.length > 0 && (
            <nav aria-label={`${sectionRoot.name} subcategories`} className="mt-5 flex flex-wrap gap-2">
              {[{ c: sectionRoot, label: `All ${sectionRoot.name}`, n: null as number | null }, ...chipCategories.map((c, i) => ({ c, label: c.name, n: chipCounts[i] }))]
                .map(({ c, label, n }) => {
                  const active = selectedCategory?.id === c.id
                  return (
                    <Link key={c.id}
                      href={`${buildHref({ ...params, category: c.slug, page: undefined })}#products`}
                      aria-current={active ? "page" : undefined}
                      className={`inline-flex items-center gap-1.5 rounded-full border px-3.5 py-1.5 text-sm transition-colors ${
                        active ? "border-primary bg-primary text-primary-foreground" : "hover:bg-muted"
                      }`}>
                      {label}
                      {n !== null && <span className={`text-xs tabular-nums ${active ? "text-primary-foreground/70" : "text-muted-foreground"}`}>{n}</span>}
                    </Link>
                  )
                })}
            </nav>
          )}

          {(products?.length ?? 0) > 0 ? (
            <div className="mt-6 grid grid-cols-2 gap-x-4 gap-y-8 md:grid-cols-3 md:gap-x-6 xl:grid-cols-4">
              {products.map((product) => (
                <Item key={product.id} itemData={product} />
              ))}
            </div>
          ) : null}

          {totalPages > 1 && (
            <nav aria-label="Pages" className="mt-12 flex flex-wrap items-center justify-center gap-1 text-sm">
              {pageNumber > 1 && (
                <Link href={`${buildHref({ ...params, page: String(pageNumber - 1) })}#products`}
                  className="rounded-full px-3 py-1.5 text-muted-foreground hover:bg-muted hover:text-foreground">
                  Previous
                </Link>
              )}
              {pageWindow(pageNumber, totalPages).map((p, i) => p === null ? (
                <span key={`gap-${i}`} className="px-2 text-muted-foreground">…</span>
              ) : (
                <Link key={p} href={`${buildHref({ ...params, page: p === 1 ? undefined : String(p) })}#products`}
                  aria-current={p === pageNumber ? "page" : undefined}
                  className={`min-w-9 rounded-full px-3 py-1.5 text-center transition-colors ${
                    p === pageNumber ? "bg-primary text-primary-foreground" : "text-muted-foreground hover:bg-muted hover:text-foreground"
                  }`}>
                  {p}
                </Link>
              ))}
              {pageNumber < totalPages && (
                <Link href={`${buildHref({ ...params, page: String(pageNumber + 1) })}#products`}
                  className="rounded-full px-3 py-1.5 text-muted-foreground hover:bg-muted hover:text-foreground">
                  Next
                </Link>
              )}
            </nav>
          )}

          {(products?.length ?? 0) === 0 && (
            <div className="mt-20 text-center">
              <p className="font-medium">No products match these filters</p>
              <Link href="/" className="mt-2 inline-block text-sm underline">
                Clear filters
              </Link>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}

