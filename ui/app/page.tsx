import Image from "next/image";
import Item from "@/components/Item/item";
import { itemData } from "@/components/Item/types/itemType";
import Link from "next/link";
import { buildHref, getCategories, sortOptions, toApiSort } from "@/lib/temp";
import FilterSidebar from "@/components/Navbar/filterSidebar";

type ShopParams = {
  category?: string;
  sort?: string;
  minPrice?: string;
  maxPrice?: string;
};


async function fetchProducts(params: ShopParams): Promise<itemData[]> {
  const apiSort = toApiSort(params.sort)
  const query = new URLSearchParams({
    ...(params.category ? { CategoryId: params.category } : {}),
    ...(params.minPrice ? { MinPrice: params.minPrice } : {}),
    ...(params.maxPrice ? { MaxPrice: params.maxPrice } : {}),
    ...apiSort,
  })
  const res = await fetch(`http://localhost:7000/api/v1/products?${query}`, {next: {revalidate: 60}})
  
  const data = await res.json()
  return data.items
}


export default async function Home({
  searchParams,
}: {
  searchParams: Promise<ShopParams>;
}) {
  const params = await searchParams;
  const products = await fetchProducts(params);
  const categories = await getCategories()
 
  const activeSort = params.sort ?? "newest";
  const title =
    categories.find((c:any) => c.slug === params.category)?.label ?? "All products";
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
          <div className="flex flex-wrap items-end justify-between gap-4">
            <div>
              <h2 className="text-2xl font-semibold tracking-tight">{title}</h2>
              <p className="mt-1 text-sm text-muted-foreground">
                {products?.length ?? 0} {(products?.length ?? 0) === 1 ? "product" : "products"}
              </p>
            </div>
 
            <nav aria-label="Sort products" className="flex flex-wrap gap-1 text-sm">
              {sortOptions.map((o) => {
                const active = activeSort === o.value;
                return (
                  <Link
                    key={o.value}
                    href={`${buildHref({ ...params, sort: o.value })}#products`}
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
 
          {(products?.length ?? 0) > 0 ? (
            <div className="mt-6 grid grid-cols-2 gap-x-4 gap-y-8 md:grid-cols-3 md:gap-x-6 xl:grid-cols-4">
              {products.map((product) => (
                <Item key={product.id} itemData={product} />
              ))}
            </div>
          ) : (
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

