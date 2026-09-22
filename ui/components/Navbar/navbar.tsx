import { Menu, User, ShoppingCart, Search } from "lucide-react";
import Link from "next/link";
import { DropdownMenu, DropdownMenuContent, DropdownMenuGroup, DropdownMenuItem, DropdownMenuLabel, DropdownMenuTrigger } from "../ui/dropdown-menu";
import { getSession } from "@/lib/session";
import { buildHref, getCategories } from "@/lib/temp";


async function Navbar() {
  const session = await getSession();
  const categories = await getCategories()
 
  const actionLink =
    "flex items-center gap-2 rounded-md px-2 py-2 text-sm font-medium transition-colors hover:bg-muted";
 
  return (
    <header className="sticky top-0 z-40 border-b bg-background/95 backdrop-blur">
      {/* Row 1: logo, search, account, basket */}
      <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-x-6 gap-y-3 px-4 py-3 sm:px-6 lg:px-8">
        <Link href="/" className="text-2xl font-semibold tracking-tight">
          EShop
        </Link>
 
        {/* On mobile the search drops to its own full-width row */}
        <form
          action="/search"
          role="search"
          className="relative order-3 w-full md:order-0 md:mx-auto md:max-w-xl md:flex-1"
        >
          <Search
            aria-hidden
            className="pointer-events-none absolute left-4 top-1/2 size-5 -translate-y-1/2 text-muted-foreground"
          />
          <input
            type="search"
            name="q"
            placeholder="Search products"
            aria-label="Search products"
            className="h-11 w-full rounded-full bg-muted pl-12 pr-4 text-sm outline-none placeholder:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring"
          />
        </form>
 
        <nav aria-label="Account" className="ml-auto flex items-center gap-1 md:ml-0">
          {!session ? (
            // NOTE: "/autorization" is spelled as in your original route
            <Link href="/autorization" className={actionLink}>
              <User className="size-5" />
              <span className="hidden sm:inline">Account</span>
            </Link>
          ) : (
            <div className={actionLink}>
              <User className="size-5" />
              <span className="hidden sm:inline">{session.firstName}</span>
            </div>
          )}
          <Link href="/cart" className={actionLink}>
            <ShoppingCart className="size-5" />
            <span className="hidden sm:inline">Basket</span>
          </Link>
        </nav>
      </div>
 
      {/* Row 2: categories (scrolls sideways on small screens) */}
      <nav aria-label="Categories" className="border-t">
        <ul className="mx-auto flex max-w-7xl gap-1 overflow-x-auto px-4 text-sm sm:px-6 lg:px-8">
          <li>
            <Link
              href="/"
              className="block whitespace-nowrap px-2 py-2.5 text-muted-foreground transition-colors hover:text-foreground"
            >
              All
            </Link>
          </li>
          {categories.map((c:any) => (
            <li key={c.slug}>
              <Link
                href={buildHref({ category: c.slug })}
                className="block whitespace-nowrap px-2 py-2.5 text-muted-foreground transition-colors hover:text-foreground"
              >
                {c.label}
              </Link>
            </li>
          ))}
        </ul>
      </nav>
    </header>
  );
}
 
export default Navbar;
