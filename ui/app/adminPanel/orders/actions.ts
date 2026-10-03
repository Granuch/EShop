"use server";

import { revalidatePath } from "next/cache";
import { toFormState } from "@/lib/admin/actionErrors";
import { ApiError } from "@/lib/admin/api";
import { readFields, type FormState } from "@/lib/admin/forms";
import {
  addOrderItem,
  addOrderNote,
  cancelOrder,
  getOrder,
  removeOrderItem,
  transitionOrder,
  updateOrderItemQuantity,
  updateShippingAddress,
} from "@/lib/admin/ordering";

const ORDERS_PATH = "/adminPanel/orders";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Ship (Paid → Shipped, emails the customer) or deliver (Shipped → Delivered). A 409 names the wrong state when the
 * order is already further along (F-51: shipping a Shipped order says "must be paid"), so the order is re-read and
 * its real status reported instead of the API's message.
 */
export async function transitionOrderAction(orderId: string, transition: "ship" | "deliver"): Promise<FormState> {
  if (!GUID.test(orderId)) return { status: "error", message: "Unknown order." };
  const detailPath = `${ORDERS_PATH}/${orderId}`;
  const verb = transition === "ship" ? "shipped" : "delivered";

  try {
    await transitionOrder(orderId, transition);
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) {
      let current: string | null = null;
      try {
        current = (await getOrder(orderId)).status;
      } catch {
        // Report the conflict without the status rather than fail the action.
      }
      revalidatePath(detailPath);
      return {
        status: "error",
        message: current
          ? `This order is ${current} now, so it cannot be marked as ${verb}. The page shows its current state.`
          : "The order changed in the meantime. Reload the page to see its current state.",
      };
    }
    return toFormState(error, { fields: [], service: "ordering" });
  }

  revalidatePath(detailPath);
  revalidatePath(ORDERS_PATH);
  return { status: "ok", message: `Marked as ${verb}.` };
}

/**
 * POST /notes. The body is sent trimmed: the API counts its 2000-character limit BEFORE trimming, so a padded but
 * otherwise valid note would be refused. Empty and over-long bodies are the API's to refuse (key `body`).
 */
export async function addOrderNoteAction(orderId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["body"] as const);
  if (!GUID.test(orderId)) return { status: "error", message: "Unknown order.", values };
  try {
    await addOrderNote(orderId, { body: values.body });
  } catch (error) {
    return toFormState(error, { fields: ["body"], service: "ordering", values });
  }
  revalidatePath(`${ORDERS_PATH}/${orderId}`);
  return { status: "ok", message: "Note added." };
}

function revalidateOrder(orderId: string) {
  revalidatePath(`${ORDERS_PATH}/${orderId}`);
  revalidatePath(ORDERS_PATH);
}

/** 409s here name the order's real state ("…; this order is paid."), so their detail is shown without "reload". */
function conflictDetail(error: unknown, state: FormState): FormState {
  return error instanceof ApiError && error.status === 409 && error.problem?.detail
    ? { ...state, message: error.problem.detail }
    : state;
}

/** POST /cancel with the admin's reason. Pending only; the order's pending payment is cancelled shortly after. */
export async function cancelOrderAction(orderId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["reason"] as const);
  if (!GUID.test(orderId)) return { status: "error", message: "Unknown order.", values };
  try {
    await cancelOrder(orderId, { reason: values.reason });
  } catch (error) {
    return conflictDetail(error, toFormState(error, { fields: ["reason"], service: "ordering", values }));
  }
  revalidateOrder(orderId);
  return { status: "ok", message: "Order cancelled." };
}

function parseQuantity(value: string): number | null {
  return /^\d{1,9}$/.test(value) && Number(value) > 0 ? Number(value) : null;
}

/** PUT /items/{itemId}: the line keeps its unit price; the total and the payment amount follow. */
export async function updateItemQuantityAction(
  orderId: string,
  itemId: string,
  _state: FormState,
  formData: FormData,
): Promise<FormState> {
  const values = readFields(formData, ["quantity"] as const);
  if (!GUID.test(orderId) || !GUID.test(itemId)) return { status: "error", message: "Unknown line.", values };
  const quantity = parseQuantity(values.quantity);
  if (quantity === null) return { status: "error", message: "Enter a whole number above 0.", values };
  try {
    await updateOrderItemQuantity(orderId, itemId, { quantity });
  } catch (error) {
    return conflictDetail(error, toFormState(error, { fields: [], service: "ordering", values }));
  }
  revalidateOrder(orderId);
  return { status: "ok", message: "Saved." };
}

/** DELETE /items/{itemId}. The last line is refused by the API ("Order must have at least one item."). */
export async function removeItemAction(orderId: string, itemId: string): Promise<FormState> {
  if (!GUID.test(orderId) || !GUID.test(itemId)) return { status: "error", message: "Unknown line." };
  try {
    await removeOrderItem(orderId, itemId);
  } catch (error) {
    return conflictDetail(error, toFormState(error, { fields: [], service: "ordering" }));
  }
  revalidateOrder(orderId);
  return { status: "ok", message: "Line removed." };
}

/** POST /items: an Active product not yet on the order, priced from Catalog now. */
export async function addItemAction(orderId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["productId", "quantity"] as const);
  if (!GUID.test(orderId)) return { status: "error", message: "Unknown order.", values };
  const fieldErrors: Record<string, string[]> = {};
  if (!GUID.test(values.productId)) fieldErrors.productId = ["Choose a product (or paste its id)."];
  const quantity = parseQuantity(values.quantity);
  if (quantity === null) fieldErrors.quantity = ["Enter a whole number above 0."];
  if (Object.keys(fieldErrors).length > 0) return { status: "error", message: "Check the highlighted fields.", fieldErrors, values };
  try {
    await addOrderItem(orderId, { productId: values.productId, quantity: quantity! });
  } catch (error) {
    return conflictDetail(
      error,
      toFormState(error, {
        fields: ["productId", "quantity"],
        codeFields: { "Order.ProductUnavailable": "productId", DomainError: "productId" },
        service: "ordering",
        values,
      }),
    );
  }
  revalidateOrder(orderId);
  return { status: "ok", message: "Product added." };
}

const ADDRESS_FIELDS = ["street", "city", "state", "zipCode", "country"] as const;

/** PUT /shipping-address: all five fields, a full replacement. Pending or Paid only. */
export async function updateAddressAction(orderId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ADDRESS_FIELDS);
  if (!GUID.test(orderId)) return { status: "error", message: "Unknown order.", values };
  try {
    await updateShippingAddress(orderId, values);
  } catch (error) {
    return conflictDetail(error, toFormState(error, { fields: ADDRESS_FIELDS, service: "ordering", values }));
  }
  revalidateOrder(orderId);
  return { status: "ok", message: "Address saved." };
}
