import { cookies, headers } from "next/headers";

// Server-only: only the Next server calls the gateway, so this is never NEXT_PUBLIC_*.
const API_BASE_URL = process.env.API_BASE_URL ?? "http://localhost:7000"

// Every call leaves from this server, so without a forwarded address the gateway and Identity's login limiter put all
// users in one bucket. Next keeps an X-Forwarded-For that the browser sends and fills it in only when it is absent,
// so the header can be trusted only when a reverse proxy in front sets it: TRUST_PROXY_HEADERS=true says one does.
// Its rightmost entry is the address that proxy saw. Without the flag nothing is forwarded, rather than a value the
// browser chose.
const TRUST_PROXY_HEADERS = process.env.TRUST_PROXY_HEADERS === "true"

const IPV4 = /^\d{1,3}(\.\d{1,3}){3}$/
const IPV6 = /^[0-9a-f:.]+$/i

async function clientAddress(): Promise<string | null> {
    if (!TRUST_PROXY_HEADERS) return null
    const last = (await headers()).get("x-forwarded-for")?.split(",").at(-1)?.trim()
    if (!last) return null
    return IPV4.test(last) || (last.includes(":") && IPV6.test(last)) ? last : null
}

// A cached response is shared by every visitor, so it carries no one's address (and the cache key stays the same).
function isShared(options: RequestInit): boolean {
    const revalidate = options.next?.revalidate
    return options.cache === "force-cache" || (revalidate !== undefined && revalidate !== 0)
}

/** A call to the gateway on behalf of the current visitor, without their token. */
export async function gatewayFetch(path: string, options: RequestInit = {}) {
    const requestHeaders = new Headers(options.headers)
    const address = isShared(options) ? null : await clientAddress()
    if (address) requestHeaders.set("X-Forwarded-For", address)

    return fetch(`${API_BASE_URL}${path}`, { ...options, headers: requestHeaders })
}

// Token refresh happens in proxy.ts: a render cannot store new cookies, so there is no retry on 401 here.
export async function apiFetch(path:string, options:RequestInit = {}) {
    const accessToken = (await cookies()).get("access_token")?.value
    const requestHeaders = new Headers(options.headers)
    if (accessToken) requestHeaders.set("Authorization", `Bearer ${accessToken}`)

    return gatewayFetch(path, { ...options, headers: requestHeaders })
}
