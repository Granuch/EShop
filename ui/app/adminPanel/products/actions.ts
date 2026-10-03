"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { toFormState } from "@/lib/admin/actionErrors";
import { createProduct } from "@/lib/admin/catalog";
import { readFields, type FormState } from "@/lib/admin/forms";
import type { CreateProductRequest } from "@/lib/admin/types/catalog";

// Server actions: each re-checks the permission inside the DAL call (a page check does not cover its actions),
// sends only the documented fields, and answers its form instead of throwing.

const PRODUCTS_PATH = "/adminPanel/products";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SKU = /^[A-Za-z0-9_-]+$/;
const CREATE_FIELDS = ["name", "sku", "price", "stockQuantity", "categoryId", "description", "imageUrls"] as const;
const PRODUCT_CODE_FIELDS = { "Product.SkuConflict": "sku", "Category.NotFound": "categoryId" };

/** Number() reads "" as 0, so emptiness is checked first; ranges are left to the API's own messages. */
function parseNumber(value: string): number | null {
  if (value === "") return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function parseInteger(value: string): number | null {
  return /^-?\d{1,10}$/.test(value) ? Number(value) : null;
}

export async function createProductAction(_state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, CREATE_FIELDS);
  const price = parseNumber(values.price);
  const stockQuantity = parseInteger(values.stockQuantity);

  // Only what the API cannot be sent at all (a wrong type would be a MalformedRequest blaming "the body", F-25),
  // plus create's SKU rule.
  const fieldErrors: Record<string, string[]> = {};
  if (price === null) fieldErrors.price = ["Enter a price."];
  if (stockQuantity === null) fieldErrors.stockQuantity = ["Enter a whole number."];
  if (!GUID.test(values.categoryId)) fieldErrors.categoryId = ["Choose a category."];
  if (values.sku && !SKU.test(values.sku)) fieldErrors.sku = ["Use only letters, digits, - and _."];
  if (Object.keys(fieldErrors).length > 0) {
    return { status: "error", message: "Check the highlighted fields.", fieldErrors, values };
  }

  const imageUrls = values.imageUrls.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const body: CreateProductRequest = {
    // Create keeps surrounding spaces, update trims them (F-41): trim here so both agree.
    name: values.name,
    sku: values.sku,
    price: price!,
    stockQuantity: stockQuantity!,
    categoryId: values.categoryId,
    ...(values.description ? { description: values.description } : {}),
    ...(imageUrls.length > 0 ? { images: imageUrls.map((url, displayOrder) => ({ url, displayOrder })) } : {}),
  };

  let id: string;
  try {
    ({ id } = await createProduct(body));
  } catch (error) {
    return toFormState(error, {
      fields: ["name", "sku", "price", "stockQuantity", "categoryId", "description", "images"],
      codeFields: PRODUCT_CODE_FIELDS,
      service: "catalog",
      values,
    });
  }

  revalidatePath(PRODUCTS_PATH);
  redirect(`${PRODUCTS_PATH}/${id}`);
}
