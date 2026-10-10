'use server'

import { apiFetch } from "./api"
import { problemMessages, readProblem } from "./problem"
import { getSession } from "./session"

// The card payment flow (docs/01-overview/frontend/payment.md, "How a customer pays"). Server actions, so the token
// stays in its httpOnly cookie and the gateway is reached through API_BASE_URL.

type OrderSummary = { id: string; status: string; createdAt: string; totalPrice: number }

/**
 * The order a checkout just created. Basket answers a checkout before Ordering has created the order (it travels as
 * a message), and nothing maps the checkout to the order id, so this looks for the newest Pending order created
 * since the checkout. `since` comes from this server's clock (fetchCheckout), never from the browser's.
 */
export async function findNewOrder(since: string): Promise<{ orderId: string | null; signedOut?: true }> {
    const session = await getSession()
    if (!session) return { orderId: null, signedOut: true }

    try {
        const res = await apiFetch(`/api/v1/users/${session.id}/orders?PageNumber=1&PageSize=10`)
        if (!res.ok) return { orderId: null }
        const page = await res.json() as { items: OrderSummary[] }
        // A little slack for clock differences between this container and Ordering's. Without a valid checkout
        // time, only an order from the last two minutes counts as new.
        const parsed = Date.parse(since)
        const from = Number.isNaN(parsed) ? Date.now() - 120_000 : parsed - 30_000
        const fresh = page.items
            .filter((o) => o.status === "Pending" && Date.parse(o.createdAt) >= from)
            .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
        return { orderId: fresh[0]?.id ?? null }
    }
    catch {
        return { orderId: null }
    }
}

export type StartPaymentResult =
    | { kind: "ready"; clientSecret: string; intentStatus: string }
    | { kind: "retry" }
    | { kind: "closed"; message: string }
    | { kind: "error"; message: string }

/**
 * Creates the Stripe payment intent for the order, or resumes the one created earlier (the same client secret comes
 * back, so a reload or a second tab is safe). The amount is always Payment's own record of the order total.
 */
export async function startPayment(orderId: string): Promise<StartPaymentResult> {
    const session = await getSession()
    if (!session) return { kind: "error", message: "Your session has expired. Please sign in again" }

    let res: Response
    try {
        res = await apiFetch("/api/v1/payments/create-intent", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ orderId, email: session.email })
        })
    }
    catch {
        return { kind: "error", message: "The shop could not be reached. Please try again" }
    }

    if (res.ok) {
        const data = await res.json() as { clientSecret: string; status: string }
        return { kind: "ready", clientSecret: data.clientSecret, intentStatus: data.status }
    }

    const problem = await readProblem(res)
    switch (problem?.errorCode) {
        // Payment records the order's payment from a message, 1 to 15 s after the order exists.
        case "PAYMENT_NOT_READY":
            return { kind: "retry" }
        case "PAYMENT_PROVIDER_UNAVAILABLE":
            return { kind: "retry" }
        case "PAYMENT_ALREADY_EXISTS":
            return { kind: "closed", message: problem.detail ?? "This order cannot be paid by card" }
        case "PAYMENT_NOT_FOUND":
            return { kind: "closed", message: "This order was not found" }
        case "STRIPE_NOT_ENABLED":
            return { kind: "closed", message: "Card payments are switched off in this shop" }
    }
    return {
        kind: "error",
        message: problemMessages(res.status, problem, res.headers.get("retry-after"), "The payment could not be started")[0]
    }
}

export type PaymentProgress = { paymentStatus: string | null; orderStatus: string | null; errorMessage: string | null }

/**
 * Where the payment and the order stand. Stripe's webhook turns the payment Success at once; the order turns Paid
 * from a message about 14 s later, so a confirmation page should trust the payment first.
 */
export async function getPaymentProgress(orderId: string): Promise<PaymentProgress> {
    const session = await getSession()
    if (!session) return { paymentStatus: null, orderStatus: null, errorMessage: null }

    const [payments, order] = await Promise.all([
        apiFetch(`/api/v1/users/${session.id}/payments?pageNumber=1&pageSize=50`).catch(() => null),
        apiFetch(`/api/v1/orders/${orderId}`).catch(() => null)
    ])

    let paymentStatus: string | null = null
    let errorMessage: string | null = null
    if (payments?.ok) {
        const page = await payments.json() as { items: { orderId: string; status: string; errorMessage: string | null }[] }
        const payment = page.items.find((p) => p.orderId.toLowerCase() === orderId.toLowerCase())
        paymentStatus = payment?.status ?? null
        errorMessage = payment?.errorMessage ?? null
    }
    const orderStatus = order?.ok ? (await order.json() as { status: string }).status : null

    return { paymentStatus, orderStatus, errorMessage }
}
