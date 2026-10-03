"use server";

import { revalidatePath } from "next/cache";
import { toFormState } from "@/lib/admin/actionErrors";
import { ApiError } from "@/lib/admin/api";
import { readFields, type FormState } from "@/lib/admin/forms";
import { addOrderNote, getOrder, transitionOrder } from "@/lib/admin/ordering";

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
