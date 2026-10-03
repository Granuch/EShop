"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { toFormState } from "@/lib/admin/actionErrors";
import type { FormState } from "@/lib/admin/forms";
import { replayDeadLetters } from "@/lib/admin/basket";

/**
 * POST /admin/outbox/dead-letters/replay. The button disappears once the count is 0, so the result travels in the
 * URL (`?replayed=n`) instead of the button's own state.
 */
export async function replayDeadLettersAction(): Promise<FormState> {
  let replayed: number;
  try {
    ({ replayed } = await replayDeadLetters());
  } catch (error) {
    return toFormState(error, { fields: [], service: "basket" });
  }
  revalidatePath("/adminPanel/baskets/outbox");
  redirect(`/adminPanel/baskets/outbox?replayed=${replayed}`);
}
