'use server'

import { apiFetch } from "@/lib/api"
import { problemMessages, readProblem } from "@/lib/problem"
import { getSession } from "@/lib/session"

export async function fetchUserOrders(pageNumber: number, pageSize: number) {
    const session = await getSession()
    if (!session) {
        return {ok: false as const, status: 401, error: "Your session has expired. Please sign in again"}
    }

    const params = new URLSearchParams({ PageNumber: String(pageNumber), PageSize: String(pageSize) })
    let res: Response
    try {
        res = await apiFetch(`/api/v1/users/${session.id}/orders?${params}`)
    }
    catch {
        return {ok: false as const, status: 503, error: "The shop could not be reached. Please try again"}
    }

    if(!res.ok) {
        const data = await readProblem(res)
        return {ok: false as const, status: res.status,
            error: problemMessages(res.status, data, res.headers.get("retry-after"), "Could not load your orders.")[0]}
    }

    return {ok: true as const, data: await res.json()}
}
