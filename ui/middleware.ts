import { NextRequest, NextResponse } from "next/server";

function isExpired(token:string):boolean {
    try {
        const payload = JSON.parse(atob(token.split(".")[1]))
        return Date.now() >= payload.exp * 1000
    }
    catch {
        return true
    }
}

export async function middleware(req: NextRequest) {
    const accessToken = req.cookies.get("access_token")?.value
    const refreshToken = req.cookies.get("refresh_token")?.value

    if(!accessToken && !refreshToken) {
        return NextResponse.redirect(new URL("/autorization", req.url))
    }

    if (accessToken && isExpired(accessToken) && refreshToken) {
        const refreshRes = await fetch(new URL("/api/auth/refresh", req.url), {
        method: "POST",
        headers: { cookie: req.headers.get("cookie") ?? "" },
        });
    
        if (!refreshRes.ok) {
        return NextResponse.redirect(new URL("/login", req.url));
        }

        const response = NextResponse.next();
        const setCookie = refreshRes.headers.get("set-cookie");
        if (setCookie) response.headers.set("set-cookie", setCookie);
        return response;
    }

    return NextResponse.next()
}

export const config = {
  matcher: ["/account/:path*", "/checkout/:path*"],
};