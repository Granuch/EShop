"use server";

import { revalidatePath } from "next/cache";
import { toFormState } from "@/lib/admin/actionErrors";
import { bulkProducts, importProducts } from "@/lib/admin/catalog";
import { parseCsv, undoFormulaGuard } from "@/lib/admin/csv";
import type { FormState } from "@/lib/admin/forms";
import type { ImportProductRow, ProductImportReport } from "@/lib/admin/types/catalog";

const PRODUCTS_PATH = "/adminPanel/products";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const MAX_ROWS = 1000;
/** The export's columns an import row takes; the rest (Id, Status, DiscountPrice, MainImageUrl, CreatedAt) are ignored. */
const REQUIRED = ["sku", "name", "price", "stockquantity", "categoryid"] as const;

export interface ImportState extends FormState {
  report?: ProductImportReport;
  /** The file row of each row sent (the header is row 1), by request index, for the report. */
  lines?: number[];
  /** Local problems found before sending; nothing was sent when this is set. */
  rowErrors?: { line: number; message: string }[];
}

/**
 * Reads the CSV (an uploaded file, else the pasted text), checks what the API cannot be sent at all, and imports. Any
 * local row problem stops the whole file, so a fixed file can be sent again without half of it already created.
 */
export async function importProductsAction(_state: ImportState, formData: FormData): Promise<ImportState> {
  const file = formData.get("file");
  const pasted = String(formData.get("csv") ?? "");
  const text = file instanceof File && file.size > 0 ? await file.text() : pasted;
  if (!text.trim()) return { status: "error", message: "Choose a CSV file or paste its contents." };

  const [header, ...data] = parseCsv(text);
  const columns = new Map(header.map((name, index) => [name.trim().toLowerCase(), index]));
  const missing = REQUIRED.filter((name) => !columns.has(name));
  if (missing.length > 0) {
    return {
      status: "error",
      message: `The first line must name the columns. Missing: ${missing.join(", ")}. Start from an export: its columns fit.`,
    };
  }
  if (data.length === 0) return { status: "error", message: "The file has a header but no rows." };
  if (data.length > MAX_ROWS) return { status: "error", message: `At most ${MAX_ROWS} rows per import; this file has ${data.length}.` };

  const cell = (row: string[], name: string) => undoFormulaGuard((row[columns.get(name) ?? -1] ?? "").trim());
  const products: ImportProductRow[] = [];
  const lines: number[] = [];
  const rowErrors: { line: number; message: string }[] = [];
  // Row numbers as a spreadsheet shows them: the header is row 1. (A quoted line break makes a row span two lines.)
  data.forEach((row, index) => {
    const line = index + 2;
    const price = Number(cell(row, "price"));
    const stock = cell(row, "stockquantity");
    const categoryId = cell(row, "categoryid");
    const problems: string[] = [];
    if (!cell(row, "price") || !Number.isFinite(price)) problems.push("Price is not a number");
    if (!/^-?\d{1,10}$/.test(stock)) problems.push("StockQuantity is not a whole number");
    if (!GUID.test(categoryId)) problems.push("CategoryId is not an id");
    if (problems.length > 0) {
      rowErrors.push({ line, message: problems.join("; ") });
      return;
    }
    const description = cell(row, "description");
    products.push({
      sku: cell(row, "sku"),
      name: cell(row, "name"),
      price,
      stockQuantity: Number(stock),
      categoryId,
      ...(description ? { description } : {}),
    });
    lines.push(line);
  });
  if (rowErrors.length > 0) {
    return { status: "error", message: "Nothing was imported. Fix these rows and send the file again:", rowErrors };
  }

  let report: ProductImportReport;
  try {
    report = await importProducts({ products });
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  if (report.created > 0) revalidatePath(PRODUCTS_PATH);
  return {
    status: report.failed === 0 ? "ok" : "error",
    message:
      report.failed === 0
        ? `Created ${report.created} of ${report.requested}, as drafts.`
        : `Created ${report.created} of ${report.requested}, as drafts. ${report.failed} refused:`,
    report,
    lines,
  };
}

/** Publishes what an import just created, through bulk/publish (one request, the shared bulk bucket). */
export async function publishImportedAction(productIds: string[]): Promise<FormState> {
  const ids = productIds.filter((id) => GUID.test(id)).slice(0, MAX_ROWS);
  if (ids.length === 0) return { status: "error", message: "Nothing to publish." };
  try {
    const report = await bulkProducts("publish", { productIds: ids });
    revalidatePath(PRODUCTS_PATH);
    revalidatePath("/");
    return report.failed === 0
      ? { status: "ok", message: `Published ${report.succeeded}.` }
      : {
          status: "error",
          message: `Published ${report.succeeded} of ${report.requested}. ${report.items
            .filter((item) => !item.succeeded)
            .map((item) => item.error ?? item.errorCode)
            .join(" ")}`,
        };
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
}
