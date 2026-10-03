import { unstable_rethrow } from "next/navigation";
import { type NextRequest, NextResponse } from "next/server";
import { classifyFailure } from "@/lib/admin/api";
import { PermissionDeniedError } from "@/lib/admin/auth";
import { exportProducts } from "@/lib/admin/catalog";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import { buildAdminHref } from "@/lib/admin/href";
import { parseProductFilters, PRODUCTS_PATH, toLinkParams, toProductListQuery } from "../filters";

/**
 * GET /adminPanel/products/export?<the list's filters>: the API's CSV for exactly what the list shows (every page,
 * drafts included, at most 10 000 rows), streamed with its own filename. The browser never calls the gateway itself,
 * so the download goes through here. A failure returns to the list with the reason in `exportError`.
 */
export async function GET(request: NextRequest) {
  const filters = parseProductFilters(Object.fromEntries(request.nextUrl.searchParams));
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { pageNumber, pageSize, ...query } = toProductListQuery(filters);

  const back = (message: string) =>
    NextResponse.redirect(
      new URL(buildAdminHref(PRODUCTS_PATH, { ...toLinkParams(filters), exportError: message }), request.url),
      303,
    );

  let response: Response;
  try {
    response = await exportProducts(query);
  } catch (error) {
    // A signed-out session redirects to sign-in from inside the DAL: let that through.
    unstable_rethrow(error);
    if (error instanceof PermissionDeniedError) return back(error.message);
    const failure = classifyFailure(error);
    if (failure?.kind === "forbidden") return back(`This account may not export products. ${ADMIN_ROLE_HINTS.catalog}`);
    // 400 Products.ExportTooLarge carries the count in `detail`; 429 is the shared bulk bucket.
    if (failure?.kind === "invalid" || failure?.kind === "rateLimited") return back(`Export failed: ${failure.message}`);
    return back("Export failed: the catalog service could not be reached. Try again in a moment.");
  }

  return new Response(response.body, {
    headers: {
      "Content-Type": response.headers.get("content-type") ?? "text/csv; charset=utf-8",
      "Content-Disposition":
        response.headers.get("content-disposition") ?? `attachment; filename="products-${Date.now()}.csv"`,
      "Cache-Control": "no-store",
    },
  });
}
