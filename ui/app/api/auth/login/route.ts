import { NextResponse } from "next/server";
import { gatewayFetch } from "@/lib/api";
import { ACCESS_COOKIE, REFRESH_COOKIE, accessCookieOptions, refreshCookieOptions } from "@/lib/authCookies";

export async function POST(req: Request): Promise<NextResponse> {
    const body = await req.json()

    let res: Response
    try {
        res = await gatewayFetch("/api/v1/auth/login", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify(body)
        })
    }
    catch {
        return NextResponse.json({error: "The sign-in service could not be reached"}, {status: 503})
    }

    // A 429 or a gateway error may carry no JSON body.
    const data = await res.json().catch(() => null)

    if(!res.ok) {
        const failure = NextResponse.json(data ?? {error: "Sign-in failed"}, {status: res.status})
        const retryAfter = res.headers.get("retry-after")
        if(retryAfter) failure.headers.set("Retry-After", retryAfter)
        return failure
    }

    // The password was right but the account has 2FA on: the tokens are empty until the same request comes back
    // with twoFactorCode, so no cookie is set yet.
    if(data.requires2FA) {
        return NextResponse.json({requires2FA: true})
    }

    const response = NextResponse.json({user: data.user})

    setAuthCookies(response, data.accessToken, data.refreshToken, data.expiresIn)

    return response
}

export function setAuthCookies(res: NextResponse, accesToken:string, refreshToken:string, expiresIn:number) {
    res.cookies.set(ACCESS_COOKIE, accesToken, accessCookieOptions(expiresIn))
    res.cookies.set(REFRESH_COOKIE, refreshToken, refreshCookieOptions)
}
