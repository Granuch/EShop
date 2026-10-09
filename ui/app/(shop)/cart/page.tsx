import { getSession } from "@/lib/session";
import Link from "next/link";
import React from "react";
import CartItem from "./cartItem";
import OrderForm from "./orderForm";
import { basketItem, basketRes } from "./types";
import { apiFetch } from "@/lib/api";

async function getBasket(sessionId: string): Promise<basketRes> {
  const res = await apiFetch(`/api/v1/basket/${sessionId}`);

  if (!res.ok) {
    return { items: [], totalPrice: 0 } as unknown as basketRes;
  }

  return await res.json();
}

async function Page() {
  const session = await getSession();

  if (session == null) {
    return (
      <div className="flex flex-col justify-center items-center gap-4 my-16">
        <h1 className="text-3xl">Your aren`t logged in</h1>
        <p className="text-sm">Log in to add items to your basket</p>
        <Link href="/autorization">
          <div className="bg-black text-white py-2.5 px-24 text-lg hover:opacity-75">
            Log in
          </div>
        </Link>
      </div>
    );
  } else {
    const data = await getBasket(session.id);
    const basketCount: Array<basketItem> = data.items;

    if (basketCount.length === 0) {
      return (
        <div className="flex flex-col justify-center items-center gap-4 my-16">
          <h1 className="text-3xl">Your cart is empty</h1>
          <p className="text-sm">add items to your cart</p>
          <Link href="/">
            <div className="bg-black text-white py-2.5 px-24 text-lg hover:opacity-75">
              View items
            </div>
          </Link>
        </div>
      );
    } else {
      return (
      <div className="mx-4 mt-12 flex flex-col gap-8 2k:mx-40 lg:flex-row lg:items-start">
        <div className="flex w-full min-w-0 flex-col lg:w-2/3">
          <h1 className="mb-4 text-3xl font-semibold">Cart</h1>
          {basketCount.map((item) => (
            <CartItem key={item.productId} prop={item} />
          ))}
        </div>
        <aside className="w-full lg:sticky lg:top-6 lg:w-1/3">
          <OrderForm data={data} />
        </aside>
      </div>
    );
    }
  }
}

export default Page;
