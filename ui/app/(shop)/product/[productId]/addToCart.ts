'use server'

import { getSession } from "@/lib/session";
import { apiFetch } from "@/lib/api";

export async function addToCart(productId:string) {
    const session = await getSession()

    if(!session) {
        return {
            success: false,
            error: "UNAUTHORIZED",
            message: "Only authorized users can add items to the cart"
        }
      }
    
    const body = {
        ProductId: productId,
        Quantity: 1
    }

    const res = await apiFetch(`/api/v1/basket/${session.id}/items`, {
        method: "POST",
        body: JSON.stringify(body),
        headers: {"Content-Type": "application/json"}
    })
    
    if(!res.ok) throw new Error("Error");
    return {success:true}
}