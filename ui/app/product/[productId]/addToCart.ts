'use server'

import { getSession } from "@/lib/session";
import { cookies } from "next/headers";

export async function addToCart(productId:string) {
    const session = await getSession()
    const token = (await cookies()).get("access_token")?.value
    
    const body = {
        ProductId: productId,
        Quantity: 1
    }

    const res = await fetch(`http://localhost:7000/api/v1/basket/${session.id}/items`, {
        method: "POST",
        body: JSON.stringify(body),
        headers: {"Content-Type": "application/json", Authorization: `Bearer ${token}`}
    })
    
    if(!res.ok) throw new Error("Error");
    return {success:true}
}