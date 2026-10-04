"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { toFormState } from "@/lib/admin/actionErrors";
import { ApiError } from "@/lib/admin/api";
import { readFields, type FormState } from "@/lib/admin/forms";
import { refundPayment, replayFailedWebhooks, settleOffline, settleWithSimulator } from "@/lib/admin/payment";
import type { FailedStripeWebhookReplayReport } from "@/lib/admin/types/payment";

// Payment actions. Each re-checks its permission in the DAL. A 409 here names the payment's real state
// ("Only a pending payment can be settled."), so its detail is shown as it is.

const PAYMENTS_PATH = "/adminPanel/payments";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function revalidatePayment(paymentId: string, orderId: string) {
  revalidatePath(PAYMENTS_PATH);
  revalidatePath(`${PAYMENTS_PATH}/${paymentId}`);
  // The order changes by message seconds later; its page shows that on the next load.
  revalidatePath(`/adminPanel/orders/${orderId}`);
}

function withConflictDetail(error: unknown, state: FormState): FormState {
  return error instanceof ApiError && error.status === 409 && error.problem?.detail
    ? { ...state, message: error.problem.detail }
    : state;
}

/** POST /offline: settles a Pending payment for its recorded amount; the reference must be unique. */
export async function settleOfflineAction(paymentId: string, orderId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["reference"] as const);
  if (!GUID.test(paymentId) || !GUID.test(orderId)) return { status: "error", message: "Unknown payment.", values };
  try {
    await settleOffline({ orderId, reference: values.reference });
  } catch (error) {
    return withConflictDetail(
      error,
      toFormState(error, { fields: ["reference"], codeFields: { PAYMENT_REFERENCE_IN_USE: "reference" }, values }),
    );
  }
  revalidatePayment(paymentId, orderId);
  redirect(`${PAYMENTS_PATH}/${paymentId}?done=offline`);
}

/** POST /api/v1/payments: the simulator's outcome may be Failed, which cancels the order. */
export async function settleSimulatorAction(paymentId: string, orderId: string): Promise<FormState> {
  if (!GUID.test(paymentId) || !GUID.test(orderId)) return { status: "error", message: "Unknown payment." };
  let status: string;
  try {
    ({ status } = await settleWithSimulator({ orderId }));
  } catch (error) {
    return withConflictDetail(error, toFormState(error, { fields: [], service: "payment" }));
  }
  revalidatePayment(paymentId, orderId);
  redirect(`${PAYMENTS_PATH}/${paymentId}?done=${status === "Success" ? "simulated" : "simulatedFailed"}`);
}

/** POST /{id}/refund in full (no amount sent); the reason becomes the payment's errorMessage. */
export async function refundAction(paymentId: string, orderId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["reason"] as const);
  if (!GUID.test(paymentId)) return { status: "error", message: "Unknown payment.", values };
  try {
    await refundPayment(paymentId, { reason: values.reason || null });
  } catch (error) {
    return withConflictDetail(error, toFormState(error, { fields: ["reason"], service: "payment", values }));
  }
  revalidatePayment(paymentId, orderId);
  redirect(`${PAYMENTS_PATH}/${paymentId}?done=refunded`);
}

export interface ReplayState extends FormState {
  report?: FailedStripeWebhookReplayReport;
}

/** POST /webhooks/failed/replay with no ids: every outstanding capture, oldest first, up to 100. */
export async function replayWebhooksAction(): Promise<ReplayState> {
  try {
    const report = await replayFailedWebhooks({});
    revalidatePath(PAYMENTS_PATH);
    return {
      status: report.stillFailing > 0 ? "error" : "ok",
      message:
        report.attempted === 0
          ? "Nothing to replay: no failed Stripe deliveries are waiting."
          : `Replayed ${report.replayed} of ${report.attempted}; ${report.stillFailing} still failing; ${report.outstanding} waiting.`,
      report,
    };
  } catch (error) {
    return toFormState(error, { fields: [] });
  }
}
