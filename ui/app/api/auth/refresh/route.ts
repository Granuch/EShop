import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { gatewayFetch } from "@/lib/api";
import { setAuthCookies } from "../login/route";

// 401 means "sign the user out", and proxy.ts acts on it. Only a refused token earns it: a rate limit or an outage
// keeps its own status (and Retry-After), so the session survives until the next try.
export async function POST() {
    const refreshToken = (await cookies()).get("refresh_token")?.value
    if(!refreshToken) {
        return NextResponse.json({error: "No refresh token"}, {status:401})
    }

    let res: Response
    try {
        res = await gatewayFetch("/api/v1/auth/refresh-token", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({RefreshToken: refreshToken})
        })
    }
    catch {
        return NextResponse.json({error: "The sign-in service could not be reached"}, {status: 503})
    }

    if(res.status === 400 || res.status === 401) {
        return NextResponse.json({error: "The refresh token was refused"}, {status: 401})
    }
    if(!res.ok) {
        const failure = NextResponse.json({error: "The token could not be refreshed"}, {status: res.status})
        const retryAfter = res.headers.get("retry-after")
        if(retryAfter) failure.headers.set("Retry-After", retryAfter)
        return failure
    }

    const data = await res.json()
    const response = NextResponse.json({ok: true})

    setAuthCookies(response, data.accessToken, data.refreshToken, data.expiresIn)

    return response
}
