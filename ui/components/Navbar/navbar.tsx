import { Menu, User, ShoppingCart } from "lucide-react";
import Link from "next/link";

function Navbar() {
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
          <div className="w-full flex justify-center">
            <input
            type="text"
            placeholder="Пошук..."
            className="px-10 py-3.5 w-1/2 bg-gray-100"
          />
          </div>
          <div className="flex gap-5">
            <Link
              href="/"
              className="flex gap-1 text-base hover:cursor-pointer hover:underline"
            >
              <User />
              <div>Акаунт</div>
            </Link>
            <Link
              href="/"
              className="flex gap-1 text-base hover:cursor-pointer hover:underline"
            >
              <ShoppingCart />
              <div>Кошик</div>
            </Link>
          </div>
        </div>
      </div>
    </nav>
  );
}

export default Navbar;
