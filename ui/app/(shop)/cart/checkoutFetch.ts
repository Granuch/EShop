'use server'
import { apiFetch } from "@/lib/api";
import { problemMessages, readProblem } from "@/lib/problem";
import { getSession } from "@/lib/session";

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

// A server action: it reaches the gateway through API_BASE_URL with the visitor's token, never through localhost
// (inside the container nothing listens there).
export async function fetchCheckout(req: checkoutObj) {
    const session = await getSession()
    if (!session) {
        return {ok: false as const, status: 401, error: "Your session has expired. Please sign in again"}
    }

    // Ordering creates the order from a message after this answers; the payment page finds it as the newest order
    // since this instant, taken from this server's clock rather than the visitor's.
    const checkedOutAt = new Date().toISOString()
    let res: Response
    try {
        res = await apiFetch(`/api/v1/basket/${session.id}/checkout`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(req)
        })
    }
    catch {
        return {ok: false as const, status: 503, error: "The shop could not be reached. Please try again"}
    }

    if(!res.ok) {
        const data = await readProblem(res)
        const messages = problemMessages(res.status, data, res.headers.get("retry-after"), "The order could not be placed")
        return {ok: false as const, status: res.status, error: messages.join(" ") }
    }

    return {ok: true as const, data: await res.json().catch(() => null), checkedOutAt}
}
