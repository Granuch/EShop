import { getSession } from "@/lib/session";
import Link from "next/link";
import React from "react";
import CartItem from "./cartItem";
import OrderForm from "./orderForm";
import { basketItem, basketRes } from "./types";
import { cookies } from "next/headers";

async function getBasket(sessionId: string): Promise<basketRes> {
    const accessToken = (await cookies()).get("access_token")?.value
  const res = await fetch(`http://localhost:7000/api/v1/basket/${sessionId}`, {
    method: "GET",
    headers: {
        "Authorization": `Bearer ${accessToken}`
    }
  });

  if(!res.ok) {
    return {items:[], totalPrice: 0} as unknown as basketRes
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

    if(basketCount.length === 0 ) {
        return (
            <div>
                <div>Your cart is empty</div>
            </div>
        );
    }
    else {
        return (
        <div className="flex flex-col 2k:mx-40 mx-4 mt-12 sm:flex-row">
            <div className="flex flex-col w-full sm:w-2/3 sm:h-screen">
                <h1 className="text-3xl font-semibold mb-4 mx-6">Cart</h1>
                {basketCount.map((item) => (
                    <CartItem key={item.productId} prop={item}/>
                ))}
            </div>
            <div className="bg-gray-200 h-screen w-full sm:w-1/3">
                <OrderForm/>
            </div>
        </div>
        );
    }
  }  
}

export default Page;
