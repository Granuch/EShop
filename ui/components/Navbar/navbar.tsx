import { Menu, User, ShoppingCart, Search } from "lucide-react";
import Link from "next/link";
import { DropdownMenu, DropdownMenuContent, DropdownMenuGroup, DropdownMenuItem, DropdownMenuLabel, DropdownMenuTrigger } from "../ui/dropdown-menu";


function Navbar() {

  const isAutorized: boolean = false
  
  return (
    <nav className="">
      <div className="flex flex-col justify-center gap-2.5 pb-10">
        <div className="flex justify-between px-12 py-3 items-center">
          {/* <div>
            <Menu />
          </div> */}
          <Link href="/">
              <div className="text-3xl">EShop</div>
          </Link>
          <div className="w-full flex justify-center items-center">
            <Search color="#c0c0c0" className="translate-x-9"/>
            <input
            type="text"
            placeholder="Search..."
            className="px-13 py-3.5 w-1/2 bg-gray-100"
          />
          </div>
          <div className="flex gap-5">
            {!isAutorized ? (
              <Link
                href="/autorization"
                className="flex gap-1 text-base hover:cursor-pointer hover:underline"
              >
                <User />
                <div>Acount</div>
              </Link>
            ) : (
              <div></div>
            )}
            <Link
              href="/"
              className="flex gap-1 text-base hover:cursor-pointer hover:underline"
            >
              <ShoppingCart />
              <div>Basket</div>
            </Link>
          </div>
        </div>
      </div>
    </nav>
  );
}

export default Navbar;
