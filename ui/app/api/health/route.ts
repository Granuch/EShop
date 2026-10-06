// Liveness for the container HEALTHCHECK: answers without calling the gateway, so a probe every 30 s costs nothing
// against the gateway's per-IP rate limit and a backend outage does not restart the UI.
export function GET() {
    return Response.json({ status: "Healthy" }, { headers: { "Cache-Control": "no-store" } })
}
