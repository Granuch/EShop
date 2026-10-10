import { NextRequest, NextResponse } from "next/server";
import { gatewayFetch } from "@/lib/api";

// The navbar search runs in the browser, which cannot reach the gateway (it is not published to the internet), so it
// asks this route, which calls the gateway on the compose network. Anonymous on purpose: the search shows what any
// visitor may see, whoever is signed in.
export async function GET(req: NextRequest): Promise<NextResponse> {
    const term = req.nextUrl.searchParams.get("q")?.trim() ?? ""
    if (term.length < 2) return NextResponse.json({ items: [], totalCount: 0 })

    const params = new URLSearchParams({ SearchTerm: term.slice(0, 100), PageSize: "5" })
    let res: Response
    try {
        res = await gatewayFetch(`/api/v1/products?${params}`)
    }
    catch {
        return NextResponse.json({ error: "The catalog could not be reached" }, { status: 503 })
    }

    const data = await res.json().catch(() => null)
    const response = NextResponse.json(data ?? { error: "Search failed" }, { status: res.ok && data ? 200 : res.status })
    const retryAfter = res.headers.get("retry-after")
    if (retryAfter) response.headers.set("Retry-After", retryAfter)
    return response
}
