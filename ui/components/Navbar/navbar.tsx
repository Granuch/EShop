import { Menu, User, ShoppingCart, Search, Users, Wallet, LogOut, LockKeyhole } from "lucide-react";
import Link from "next/link";
import { DropdownMenu, DropdownMenuContent, DropdownMenuGroup, DropdownMenuItem, DropdownMenuLabel, DropdownMenuTrigger } from "../ui/dropdown-menu";
import { getSession } from "@/lib/session";
import { buildHref, getCategories } from "@/lib/temp";
import { Button } from "../ui/button";
import LogoutButton from "./logoutButton";
import SearchBar from "./search";


async function Navbar() {
  const session = await getSession();
  const categories = await getCategories()
 
  const actionLink =
    "flex items-center gap-2 rounded-md px-2 py-2 text-sm font-medium transition-colors hover:bg-muted hover:cursor-pointer";
 
  return (
    <header className="sticky top-0 z-40 border-b bg-background/95 backdrop-blur">
      {/* Row 1: logo, search, account, basket */}
      <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-x-6 gap-y-3 px-4 py-3 sm:px-6 lg:px-8">
        <Link href="/" className="text-2xl font-semibold tracking-tight">
          EShop
        </Link>
        <SearchBar />
 
        <nav aria-label="Account" className="ml-auto flex items-center gap-1 md:ml-0">
          {!session ? (
            // NOTE: "/autorization" is spelled as in your original route
            <Link href="/autorization" className={actionLink}>
              <User className="size-5" />
              <span className="hidden sm:inline">Account</span>
            </Link>
          ) : (
            <DropdownMenu>
              <DropdownMenuTrigger render={<button className={actionLink}/>}>
                  <User className="size-5" />
                  <span className="hidden sm:inline">{session.firstName}</span>
              </DropdownMenuTrigger>
              <DropdownMenuContent>
                <DropdownMenuItem render={<Link href="/account" className="hover:cursor-pointer"></Link>}>
                    <Users />
                    <span>My data</span>
                </DropdownMenuItem>
                <DropdownMenuItem render={<Link href="/orders" className="hover:cursor-pointer"></Link>}>
                  <Wallet />
                  <span>My orders</span>
                </DropdownMenuItem>
                {session.roles.includes("Admin") && 
                <DropdownMenuItem render={<Link href="/adminPanel" className="hover:cursor-pointer"></Link>}>
                  <LockKeyhole />
                  <span>Admin Panel</span>
                </DropdownMenuItem>
                }
                <DropdownMenuItem>
                  <LogOut color="#fb2c36"/>
                  <LogoutButton/>
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>         
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
