"use server";

import { revalidatePath } from "next/cache";
import { toFormState } from "@/lib/admin/actionErrors";
import { bulkProducts, type BulkProductOperation } from "@/lib/admin/catalog";
import type { FormState } from "@/lib/admin/forms";
import type { BulkProductReport } from "@/lib/admin/types/catalog";

const PRODUCTS_PATH = "/adminPanel/products";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const MAX_IDS = 1000;
const OPERATIONS: BulkProductOperation[] = ["publish", "unpublish", "delete", "category", "price"];

export interface BulkState extends FormState {
  operation?: BulkProductOperation;
  report?: BulkProductReport;
}

const DONE: Record<BulkProductOperation, string> = {
  publish: "Published",
  unpublish: "Moved to drafts",
  delete: "Deleted",
  category: "Moved",
  price: "Repriced",
};

/** Two decimals, as the API stores prices; Number.EPSILON keeps 1.005 from rounding down. */
function round2(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

/**
 * One bulk request for the ticked rows. The toolbar's button names the operation. A 200 is a per-row report, shown
 * as it is: the action never reports "done" for rows the API refused. A non-2xx refused the whole request.
 */
export async function bulkProductsAction(_state: BulkState, formData: FormData): Promise<BulkState> {
  const operation = formData.get("op") as BulkProductOperation;
  if (!OPERATIONS.includes(operation)) return { status: "error", message: "Unknown action." };

  const productIds = [...new Set(formData.getAll("ids").filter((id): id is string => typeof id === "string" && GUID.test(id)))];
  if (productIds.length === 0) return { status: "error", operation, message: "Select at least one product." };
  if (productIds.length > MAX_IDS) return { status: "error", operation, message: `Select at most ${MAX_IDS} products.` };

  let report: BulkProductReport;
  try {
    if (operation === "category") {
      const categoryId = String(formData.get("categoryId") ?? "");
      if (!GUID.test(categoryId)) return { status: "error", operation, message: "Choose the category to move them to." };
      report = await bulkProducts("category", { productIds, categoryId });
    } else if (operation === "price") {
      const mode = formData.get("priceMode") === "percent" ? "percent" : "set";
      const raw = String(formData.get("amount") ?? "").trim();
      const amount = raw === "" ? NaN : Number(raw);
      if (!Number.isFinite(amount)) {
        return { status: "error", operation, message: mode === "set" ? "Enter the new price." : "Enter a percentage, e.g. 10 or -15." };
      }
      const items = productIds.map((productId) => {
        // The list price the page showed; a percentage applies to it, not to a discount.
        const current = Number(formData.get(`price:${productId}`));
        return { productId, price: mode === "set" ? round2(amount) : round2(current * (1 + amount / 100)) };
      });
      if (items.some((item) => !(item.price > 0))) {
        return { status: "error", operation, message: "Every new price must be above 0." };
      }
      report = await bulkProducts("price", { items });
    } else {
      report = await bulkProducts(operation, { productIds });
    }
  } catch (error) {
    // Category.NotFound refuses the whole move; a 429 is the shared 10 / 60 s bulk bucket.
    return { ...toFormState(error, { fields: [], service: "catalog" }), operation };
  }

  revalidatePath(PRODUCTS_PATH);
  revalidatePath(`${PRODUCTS_PATH}/deleted`);
  revalidatePath(`${PRODUCTS_PATH}/[productId]`, "page");
  revalidatePath("/");
  const message =
    report.failed === 0
      ? `${DONE[operation]}: ${report.succeeded} of ${report.requested}.`
      : `${DONE[operation]}: ${report.succeeded} of ${report.requested}. ${report.failed} failed:`;
  return { status: report.failed === 0 ? "ok" : "error", operation, report, message };
}
