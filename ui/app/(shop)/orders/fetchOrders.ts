'use server'

import { clientFetch } from "@/lib/clientFetch"
import { getSession } from "@/lib/session"
import { cookies } from "next/headers"

export async function fetchUserOrders(pageNumber:any, PAGE_SIZE:any) {
    const session = await getSession()
    const token = (await cookies()).get("access_token")?.value

    const res = await clientFetch(`http://localhost:7000/api/v1/users/${session.id}/orders?PageNumber=${pageNumber}&PageSize=${PAGE_SIZE}`, {
        method: 'GET',
        headers: {"Authorization": `Bearer ${token}`}
    })

    const data = await res.json()

    if(!res.ok) {
        return {ok: false as const, status: res.status, error: data.detail}
    } 

    return {ok: true as const, data: data}
}