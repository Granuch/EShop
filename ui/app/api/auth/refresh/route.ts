import { ok } from "assert";
import { error } from "console";
import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { setAuthCookies } from "../login/route";

export async function POST() {
    const refreshToken = (await cookies()).get("refresh_token")?.value
    if(!refreshToken) {
        return NextResponse.json({error: "No refresh token"}, {status:401})
    }
    
    const res = await fetch("http://localhost:7000/api/v1/auth/refresh-token", {
        method: "POST",
        headers: {"Content-Type": "application/json"},
        body: JSON.stringify({RefreshToken: refreshToken})
    })

    if(!res.ok) return NextResponse.json({error: "json token failed"}, {status: 401});

    const data = await res.json()
    const response = NextResponse.json({ok: true})

    setAuthCookies(response, data.accessToken, data.refreshToken, data.expiresIn)

    return response
}