'use server'

import { getSession } from "@/lib/session";
import { apiFetch } from "@/lib/api";

export type AddToCartResult = { success: true } | { success: false, message: string }

// Basket's own details are written for API clients, so the shopper sees wording chosen here, by status.
function messageFor(status: number, errorCode: string | undefined): string {
    if (status === 401) return "Your session has expired. Please sign in again"
    if (status === 429) return "Too many requests. Please try again in a minute"
    if (errorCode === "Basket.InsufficientStock") return "Not enough of this product is in stock"
    if (errorCode === "Basket.ProductNotFound") return "This product is no longer available"
    if (errorCode === "Basket.ValidationFailed") return "Your cart cannot hold any more of this product"
    return "Could not add the product to your cart. Please try again"
}

export async function addToCart(productId: string): Promise<AddToCartResult> {
    const session = await getSession()

    if (!session) {
        return { success: false, message: "Sign in to add items to your cart" }
    }

    const res = await apiFetch(`/api/v1/basket/${session.id}/items`, {
        method: "POST",
        body: JSON.stringify({ productId, quantity: 1 }),
        headers: { "Content-Type": "application/json" }
    })

    if (res.ok) return { success: true }

    const problem = await res.json().catch(() => null)
    return { success: false, message: messageFor(res.status, problem?.errorCode) }
}
