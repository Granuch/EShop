import { cookies } from "next/headers";

// Server-only: only the Next server calls the gateway, so this is never NEXT_PUBLIC_*.
const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:7000"

// Token refresh happens in proxy.ts: a render cannot store new cookies, so there is no retry on 401 here.
export async function apiFetch(path:string, options:RequestInit = {}) {
    const accessToken = (await cookies()).get("access_token")?.value

    return fetch(`${API_BASE_URL}${path}`, {
        ...options,
        headers: {
            ...options.headers,
            ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        }
    })
}
