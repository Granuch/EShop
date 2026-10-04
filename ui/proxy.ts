import { NextRequest, NextResponse } from "next/server";

const ACCESS_COOKIE = "access_token"
const REFRESH_COOKIE = "refresh_token"
// Refresh a little before expiry; the API allows no clock skew.
const REFRESH_MARGIN_MS = 30_000

// JWT segments are base64url, which plain atob rejects ('-', '_', no padding).
function expiresAt(token: string): number | null {
    try {
        const segment = token.split(".")[1]
        const base64 = segment.replace(/-/g, "+").replace(/_/g, "/")
        const payload = JSON.parse(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, "=")))
        return typeof payload.exp === "number" ? payload.exp * 1000 : null
    }
    catch {
        return null
    }
}

function expiresWithin(token: string, ms: number): boolean {
    const exp = expiresAt(token)
    return exp === null || Date.now() + ms >= exp
}

// Next encodes cookie values with encodeURIComponent when it writes Set-Cookie.
function decodeCookieValue(value: string): string {
    try {
        return decodeURIComponent(value)
    }
    catch {
        return value
    }
}

function redirectToSignIn(req: NextRequest) {
    const response = NextResponse.redirect(new URL("/autorization", req.url))
    response.cookies.delete(ACCESS_COOKIE)
    response.cookies.delete(REFRESH_COOKIE)
    return response
}

export async function proxy(req: NextRequest) {
    const accessToken = req.cookies.get(ACCESS_COOKIE)?.value
    const refreshToken = req.cookies.get(REFRESH_COOKIE)?.value

    if (accessToken && !expiresWithin(accessToken, REFRESH_MARGIN_MS)) {
        return NextResponse.next()
    }

    if (!refreshToken) {
        // Still valid for a few seconds, and nothing to refresh it with.
        if (accessToken && !expiresWithin(accessToken, 0)) return NextResponse.next()
        return redirectToSignIn(req)
    }

    let refreshRes: Response
    try {
        // The visitor's X-Forwarded-For goes along, so the refresh is counted against their own rate limit
        // (lib/api.ts decides whether to trust it).
        const forwardedFor = req.headers.get("x-forwarded-for")
        refreshRes = await fetch(new URL("/api/auth/refresh", req.url), {
            method: "POST",
            headers: { cookie: req.headers.get("cookie") ?? "", ...(forwardedFor ? { "x-forwarded-for": forwardedFor } : {}) },
        })
    }
    catch {
        return NextResponse.next()
    }

    // 401: the refresh token was refused. Anything else (429, 5xx) is a rate limit or an outage, which must not
    // sign the user out; the page renders its own error instead.
    if (refreshRes.status === 401) return redirectToSignIn(req)
    if (!refreshRes.ok) return NextResponse.next()

    // Rewrite this request's cookies so cookies() sees the new tokens during this render,
    // and forward every Set-Cookie on its own (headers.get() would comma-join them).
    const setCookies = refreshRes.headers.getSetCookie()
    for (const setCookie of setCookies) {
        const pair = setCookie.split(";", 1)[0]
        const eq = pair.indexOf("=")
        if (eq <= 0) continue
        req.cookies.set(pair.slice(0, eq).trim(), decodeCookieValue(pair.slice(eq + 1)))
    }

    const response = NextResponse.next({ request: { headers: req.headers } })
    for (const setCookie of setCookies) response.headers.append("set-cookie", setCookie)
    return response
}

export const config = {
    // Prefetches are skipped: they cannot store refreshed cookies, and every page checks the session itself.
    matcher: [
        {
            source: "/account/:path*",
            missing: [
                { type: "header", key: "next-router-prefetch" },
                { type: "header", key: "purpose", value: "prefetch" },
            ],
        },
        {
            source: "/adminPanel/:path*",
            missing: [
                { type: "header", key: "next-router-prefetch" },
                { type: "header", key: "purpose", value: "prefetch" },
            ],
        },
    ],
};
