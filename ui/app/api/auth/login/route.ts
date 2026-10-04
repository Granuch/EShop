import { NextResponse } from "next/server";
import { gatewayFetch } from "@/lib/api";

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

    // TODO 2FA

    const response = NextResponse.json({user: data.user})

    setAuthCookies(response, data.accessToken, data.refreshToken, data.expiresIn)
    
    return response
}

export function setAuthCookies(res: NextResponse, accesToken:string, refreshToken:string, expiresIn:number) {
    res.cookies.set("access_token", accesToken, {
        httpOnly: true,
        secure: true,
        sameSite: "lax",
        path: "/",
        maxAge: expiresIn
    })

    res.cookies.set("refresh_token", refreshToken, {
        httpOnly: true,
        secure: true,
        sameSite:"lax",
        path: "/",
        maxAge: 60 * 60 * 24 * 30
    })
}