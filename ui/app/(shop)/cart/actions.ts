'use server'

import { apiFetch } from "@/lib/api";
import { getSession } from "@/lib/session";

// Runs on the server, so the access token stays in its httpOnly cookie and the call carries the visitor's address.
export async function removeFromCart(productId: string) {
    const session = await getSession()
    if (!session) return { success: false }

    const res = await apiFetch(`/api/v1/basket/${session.id}/items/${encodeURIComponent(productId)}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ quantity: 0 })
    })

    return { success: res.ok }
}
