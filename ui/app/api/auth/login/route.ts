import { getSession } from "@/lib/session";
import { NextResponse } from "next/server";

export async function POST(req: Request): Promise<NextResponse> {
    const body = await req.json()

    const res: Response = await fetch("http://localhost:7000/api/v1/auth/login", {
        method: "POST",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify(body)
    })

    const data = await res.json()

    if(!res.ok) {
        return NextResponse.json(data, {status: res.status})
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