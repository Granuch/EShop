"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { toFormState } from "@/lib/admin/actionErrors";
import { ApiError } from "@/lib/admin/api";
import {
  addAttribute,
  addImage,
  adjustStock,
  clearDiscount,
  createProduct,
  deleteAttribute,
  deleteImage,
  deleteProduct,
  getProduct,
  reorderImages,
  replaceAttributes,
  restoreProduct,
  setDiscount,
  setMainImage,
  setPublished,
  updateAttribute,
  updateImage,
  updateProduct,
} from "@/lib/admin/catalog";
import { readFields, type FormState } from "@/lib/admin/forms";
import type { AdjustStockRequest, CreateProductRequest } from "@/lib/admin/types/catalog";

// Server actions: each re-checks the permission inside the DAL call (a page check does not cover its actions),
// sends only the documented fields, and answers its form instead of throwing.

const PRODUCTS_PATH = "/adminPanel/products";
const DELETED_PATH = `${PRODUCTS_PATH}/deleted`;
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

const EDIT_FIELDS = ["name", "sku", "price", "categoryId", "description"] as const;

/** Paths that show a product: the admin list and page, and the storefront's home and product page. */
function revalidateProduct(productId: string) {
  revalidatePath(PRODUCTS_PATH);
  revalidatePath(`${PRODUCTS_PATH}/${productId}`);
  revalidatePath("/");
  revalidatePath(`/product/${productId}`);
}

/**
 * PUT replaces the stock with whatever is sent (an omitted stockQuantity is 0), and the form has no stock field. So
 * the product is re-read just before the PUT and its current stock sent back: the race window is milliseconds
 * instead of the form's lifetime (PLAN §3.4, "Stale-stock guard").
 */
export async function updateProductAction(productId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, EDIT_FIELDS);
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product.", values };

  const price = parseNumber(values.price);
  const fieldErrors: Record<string, string[]> = {};
  if (price === null) fieldErrors.price = ["Enter a price."];
  if (!GUID.test(values.categoryId)) fieldErrors.categoryId = ["Choose a category."];
  // Update does not enforce create's SKU rule (F-41); the UI does.
  if (values.sku && !SKU.test(values.sku)) fieldErrors.sku = ["Use only letters, digits, - and _."];
  if (Object.keys(fieldErrors).length > 0) {
    return { status: "error", message: "Check the highlighted fields.", fieldErrors, values };
  }

  try {
    const current = await getProduct(productId);
    await updateProduct(productId, {
      productId,
      price: price!,
      stockQuantity: current.stockQuantity,
      name: values.name,
      sku: values.sku,
      categoryId: values.categoryId,
      // "" clears the description, as the form says; null would keep it.
      description: values.description,
    });
  } catch (error) {
    return toFormState(error, {
      fields: EDIT_FIELDS,
      codeFields: PRODUCT_CODE_FIELDS,
      service: "catalog",
      values,
    });
  }

  revalidateProduct(productId);
  return { status: "ok", message: "Changes saved." };
}

/** Publish (Draft → Active) or unpublish (Active → Draft). Idempotent at the API, so a double click is harmless. */
export async function setPublishedAction(productId: string, published: boolean): Promise<FormState> {
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product." };
  try {
    await setPublished(productId, published);
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  // The storefront's home page caches the product list for 60 s; this makes the change show at once.
  revalidateProduct(productId);
  return { status: "ok", message: published ? "Published." : "Moved back to drafts." };
}

const STOCK_FIELDS = ["delta", "absolute", "reason"] as const;

/**
 * PATCH /stock with whichever of delta / absolute was filled. "Neither" and "both" are sent as they are, so the API's
 * own `$` rule answers them at form level.
 */
export async function adjustStockAction(productId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, STOCK_FIELDS);
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product.", values };

  const delta = values.delta === "" ? undefined : parseInteger(values.delta);
  const absolute = values.absolute === "" ? undefined : parseInteger(values.absolute);
  const fieldErrors: Record<string, string[]> = {};
  if (delta === null) fieldErrors.delta = ["Enter a whole number, e.g. 5 or -2."];
  if (absolute === null) fieldErrors.absolute = ["Enter a whole number."];
  if (Object.keys(fieldErrors).length > 0) {
    return { status: "error", message: "Check the highlighted fields.", fieldErrors, values };
  }

  let stockQuantity: number;
  try {
    // Built field by field: AdjustStockRequest's union cannot express "neither" or "both", which the API must judge.
    const body = {
      ...(delta !== undefined ? { delta } : {}),
      ...(absolute !== undefined ? { absolute } : {}),
      ...(values.reason ? { reason: values.reason } : {}),
    } as AdjustStockRequest;
    ({ stockQuantity } = await adjustStock(productId, body));
  } catch (error) {
    return toFormState(error, { fields: STOCK_FIELDS, service: "catalog", values });
  }

  revalidateProduct(productId);
  return { status: "ok", message: `Stock is now ${stockQuantity}.` };
}

/** Soft delete. The page it was on no longer exists afterwards, so success goes to the list. */
export async function deleteProductAction(productId: string): Promise<FormState> {
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product." };
  try {
    await deleteProduct(productId);
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  revalidateProduct(productId);
  revalidatePath(DELETED_PATH);
  redirect(PRODUCTS_PATH);
}

/**
 * Restore comes back as a Draft. A deleted category (400 Product.CategoryNotActive) or a SKU taken meanwhile (409
 * Product.SkuConflict) are answered with the API's own `detail`, which names the fix.
 */
export async function restoreProductAction(productId: string): Promise<FormState> {
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product." };
  try {
    await restoreProduct(productId);
  } catch (error) {
    const state = toFormState(error, { fields: [], service: "catalog" });
    // toFormState appends "Reload the page" to a 409; here the detail already says what to change.
    if (error instanceof ApiError && error.problem?.errorCode === "Product.SkuConflict" && error.problem.detail) {
      return { ...state, message: error.problem.detail };
    }
    return state;
  }
  revalidateProduct(productId);
  revalidatePath(DELETED_PATH);
  redirect(`${PRODUCTS_PATH}/${productId}`);
}

/** PUT /discount. Below the list price, which the API checks ("Discount price must be less than the product price."). */
export async function setDiscountAction(productId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["discountPrice"] as const);
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product.", values };
  const discountPrice = parseNumber(values.discountPrice);
  if (discountPrice === null) {
    return { status: "error", message: "Check the highlighted fields.", fieldErrors: { discountPrice: ["Enter a price."] }, values };
  }
  try {
    await setDiscount(productId, { discountPrice });
  } catch (error) {
    return toFormState(error, { fields: ["discountPrice"], service: "catalog", values });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Discount saved. Baskets are repriced shortly." };
}

/** DELETE /discount; 204 also when there is none. */
export async function clearDiscountAction(productId: string): Promise<FormState> {
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product." };
  try {
    await clearDiscount(productId);
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Discount removed." };
}

const IMAGE_FIELDS = ["url", "altText"] as const;

/** POST /images, appended after the current last image. The 11th image or a repeated URL answer a DomainError. */
export async function addImageAction(productId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, IMAGE_FIELDS);
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product.", values };
  try {
    // displayOrder defaults to 0, which would sort the new image among the first ones; put it last instead.
    const { images } = await getProduct(productId);
    const displayOrder = images.reduce((max, image) => Math.max(max, image.displayOrder + 1), 0);
    await addImage(productId, { url: values.url, altText: values.altText || null, displayOrder });
  } catch (error) {
    return toFormState(error, { fields: IMAGE_FIELDS, service: "catalog", values });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Image added." };
}

/** PUT /images/{imageId}: both fields are replaced, so an emptied alt text is sent as null and clears it. */
export async function updateImageAction(
  productId: string,
  imageId: string,
  _state: FormState,
  formData: FormData,
): Promise<FormState> {
  const values = readFields(formData, IMAGE_FIELDS);
  if (!GUID.test(productId) || !GUID.test(imageId)) return { status: "error", message: "Unknown image.", values };
  try {
    await updateImage(productId, imageId, { url: values.url, altText: values.altText || null });
  } catch (error) {
    return toFormState(error, { fields: IMAGE_FIELDS, service: "catalog", values });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Image saved." };
}

/** DELETE /images/{imageId}; the API promotes another image if this one was main. */
export async function deleteImageAction(productId: string, imageId: string): Promise<FormState> {
  if (!GUID.test(productId) || !GUID.test(imageId)) return { status: "error", message: "Unknown image." };
  try {
    await deleteImage(productId, imageId);
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Image deleted." };
}

/** PUT /images/{imageId}/main. Idempotent. */
export async function setMainImageAction(productId: string, imageId: string): Promise<FormState> {
  if (!GUID.test(productId) || !GUID.test(imageId)) return { status: "error", message: "Unknown image." };
  try {
    await setMainImage(productId, imageId);
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Main image set." };
}

/**
 * Moves one image a place earlier (-1) or later (+1). Reorder takes every image id exactly once, so the gallery is
 * re-read here and the full list sent: a list held by the page could miss an image another admin just added.
 */
export async function moveImageAction(productId: string, imageId: string, offset: -1 | 1): Promise<FormState> {
  if (!GUID.test(productId) || !GUID.test(imageId)) return { status: "error", message: "Unknown image." };
  try {
    const ids = (await getProduct(productId)).images.map((image) => image.id);
    const from = ids.indexOf(imageId);
    const to = from + offset;
    if (from < 0) return { status: "error", message: "This image was deleted meanwhile. Reload the page." };
    if (to < 0 || to >= ids.length) return { status: "ok" };
    [ids[from], ids[to]] = [ids[to], ids[from]];
    await reorderImages(productId, { imageIds: ids });
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  revalidateProduct(productId);
  return { status: "ok" };
}

const ATTRIBUTE_FIELDS = ["name", "value"] as const;
// "Attribute 'colour' already exists for this product." is a DomainError: it belongs to the name field.
const ATTRIBUTE_CODE_FIELDS = { DomainError: "name" };

/** POST /attributes: names are unique per product ignoring case; at most 50. */
export async function addAttributeAction(productId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ATTRIBUTE_FIELDS);
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product.", values };
  try {
    await addAttribute(productId, { name: values.name, value: values.value });
  } catch (error) {
    return toFormState(error, { fields: ATTRIBUTE_FIELDS, codeFields: ATTRIBUTE_CODE_FIELDS, service: "catalog", values });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Attribute added." };
}

/** PUT /attributes/{attributeId}: renames it or changes its value; both are replaced. */
export async function updateAttributeAction(
  productId: string,
  attributeId: string,
  _state: FormState,
  formData: FormData,
): Promise<FormState> {
  const values = readFields(formData, ATTRIBUTE_FIELDS);
  if (!GUID.test(productId) || !GUID.test(attributeId)) return { status: "error", message: "Unknown attribute.", values };
  try {
    await updateAttribute(productId, attributeId, { name: values.name, value: values.value });
  } catch (error) {
    return toFormState(error, { fields: ATTRIBUTE_FIELDS, codeFields: ATTRIBUTE_CODE_FIELDS, service: "catalog", values });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Attribute saved." };
}

/** DELETE /attributes/{attributeId}. */
export async function deleteAttributeAction(productId: string, attributeId: string): Promise<FormState> {
  if (!GUID.test(productId) || !GUID.test(attributeId)) return { status: "error", message: "Unknown attribute." };
  try {
    await deleteAttribute(productId, attributeId);
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  revalidateProduct(productId);
  return { status: "ok", message: "Attribute removed." };
}

/**
 * PUT /attributes with the whole set, parsed from "Name: Value" lines (split at the first colon). Blank lines are
 * skipped; an empty box sends [] and removes every attribute. Duplicate names are left to the API's rule.
 */
export async function replaceAttributesAction(productId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["attributes"] as const);
  if (!GUID.test(productId)) return { status: "error", message: "Unknown product.", values };

  const attributes: { name: string; value: string }[] = [];
  const bad: number[] = [];
  values.attributes.split(/\r?\n/).forEach((line, index) => {
    if (!line.trim()) return;
    const colon = line.indexOf(":");
    const name = colon < 0 ? "" : line.slice(0, colon).trim();
    const value = colon < 0 ? "" : line.slice(colon + 1).trim();
    if (!name || !value) bad.push(index + 1);
    else attributes.push({ name, value });
  });
  if (bad.length > 0) {
    return {
      status: "error",
      message: "Check the highlighted fields.",
      fieldErrors: { attributes: [`Line ${bad.join(", ")}: write each attribute as Name: Value.`] },
      values,
    };
  }

  try {
    await replaceAttributes(productId, { attributes });
  } catch (error) {
    return toFormState(error, { fields: ["attributes"], service: "catalog", values });
  }
  revalidateProduct(productId);
  return { status: "ok", message: attributes.length === 0 ? "All attributes removed." : "Attributes saved." };
}
