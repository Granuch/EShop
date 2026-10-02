import { cookies } from "next/headers";
import { NextResponse } from "next/server";

export async function POST() {
    const refreshToken = (await cookies()).get("refresh_token")?.value

    if(refreshToken) {
        await fetch("http://localhost:7000/api/v1/Auth/revoke-token", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({RefreshToken: refreshToken})
        })
    }

    const res = NextResponse.json({ok:true})
    res.cookies.delete("access_token")
    res.cookies.delete("refresh_token")
    return res
}