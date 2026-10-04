import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { gatewayFetch } from "@/lib/api";

export async function POST() {
    const refreshToken = (await cookies()).get("refresh_token")?.value

    if(refreshToken) {
        // Signing out locally must not depend on the gateway answering.
        await gatewayFetch("/api/v1/auth/revoke-token", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({RefreshToken: refreshToken})
        }).catch(() => undefined)
    }

    const res = NextResponse.json({ok:true})
    res.cookies.delete("access_token")
    res.cookies.delete("refresh_token")
    return res
}
