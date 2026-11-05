import { unstable_rethrow } from "next/navigation";
import { type NextRequest, NextResponse } from "next/server";
import { classifyFailure } from "@/lib/admin/api";
import { PermissionDeniedError } from "@/lib/admin/auth";
import { buildAdminHref } from "@/lib/admin/href";
import { exportPayments } from "@/lib/admin/payment";
import { parsePaymentFilters, PAYMENTS_PATH, toLinkParams, toPaymentFilterQuery } from "../filters";

/**
 * GET /adminPanel/payments/export?<the list's filters>: Payment's CSV for exactly what the list shows (every page, at
 * most 10 000 rows), streamed with its own filename. A failure returns to the list with the reason in `exportError`.
 */
export async function GET(request: NextRequest) {
  const raw: Record<string, string[]> = {};
  request.nextUrl.searchParams.forEach((value, key) => (raw[key] ??= []).push(value));
  const filters = parsePaymentFilters(raw);

  const back = (message: string) =>
    NextResponse.redirect(
      new URL(buildAdminHref(PAYMENTS_PATH, { ...toLinkParams(filters), exportError: message }), request.url),
      303,
    );

  let response: Response;
  try {
    response = await exportPayments(toPaymentFilterQuery(filters));
  } catch (error) {
    unstable_rethrow(error);
    if (error instanceof PermissionDeniedError) return back(error.message);
    const failure = classifyFailure(error);
    if (failure?.kind === "forbidden") return back("This account may not export payments.");
    // 400 EXPORT_TOO_LARGE carries the count in `detail`.
    if (failure?.kind === "invalid" || failure?.kind === "rateLimited") return back(`Export failed: ${failure.message}`);
    return back("Export failed: the payment service could not be reached. Try again in a moment.");
  }

  return new Response(response.body, {
    headers: {
      "Content-Type": response.headers.get("content-type") ?? "text/csv; charset=utf-8",
      "Content-Disposition": response.headers.get("content-disposition") ?? `attachment; filename="payments-${Date.now()}.csv"`,
      "Cache-Control": "no-store",
    },
  });
}
