'use server'
import { clientFetch } from "@/lib/clientFetch";
import { getSession } from "@/lib/session";
import { error } from "console";
import { cookies } from "next/headers";

export type checkoutData = {
  /** 3-150 chars: letters, digits, spaces, . , - / # */
  street: string;
  /** 2-100 chars: letters, spaces, . ' - */
  city: string;
  /** Same pattern as city. */
  state: string;
  /** 3-12 chars; 12345 or 12345-6789 specifically when country is US. */
  zipCode: string;
  /** ISO 3166-1 alpha-2, e.g. US. */
  country: string;
}

export type checkoutObj = {
    shippingAddress: checkoutData
}

export async function fetchCheckout(req: checkoutObj) {
    const session = await getSession()
    const token = (await cookies()).get("access_token")?.value
    const body = JSON.stringify(req)
    const res = await clientFetch(`http://localhost:7000/api/v1/basket/${session.id}/checkout`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${token}` },
        body: body
    })

    const data = await res.json()
    if(!res.ok) {
        return {ok: false as const, status: res.status, error: data.detail }
    }

    return {ok: true as const, data: data}
}