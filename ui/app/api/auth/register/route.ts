import { NextResponse } from "next/server";
import { gatewayFetch } from "@/lib/api";

// The register form used to call the gateway from the browser; through here the call reaches API_BASE_URL and
// carries the visitor's address like every other call. The gateway's status and body pass through unchanged.
export async function POST(req: Request): Promise<NextResponse> {
    const body = await req.text()

    let res: Response
    try {
        res = await gatewayFetch("/api/v1/auth/register", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body
        })
    }
    catch {
        return NextResponse.json({error: "The sign-up service could not be reached"}, {status: 503})
    }

    const data = await res.json().catch(() => null)
    const response = data === null ? new NextResponse(null, {status: res.status}) : NextResponse.json(data, {status: res.status})
    const retryAfter = res.headers.get("retry-after")
    if(retryAfter) response.headers.set("Retry-After", retryAfter)
    return response
}
