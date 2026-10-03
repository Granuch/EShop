"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { toFormState } from "@/lib/admin/actionErrors";
import { ApiError } from "@/lib/admin/api";
import { endOfDayUtc, startOfDayUtc } from "@/lib/admin/format";
import { readFields, type FormState } from "@/lib/admin/forms";
import {
  markNotificationUndeliverable,
  resendNotification,
  retryFailedNotifications,
  sendTestNotification,
} from "@/lib/admin/notification";

// Notification actions (notifications.manage). Their 409s name the row's state in plain words
// ("…is Undeliverable; nothing is attempted again once a notification is final."), so the detail is shown as it is.

const NOTIFICATIONS_PATH = "/adminPanel/notifications";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function withConflictDetail(error: unknown, state: FormState): FormState {
  return error instanceof ApiError && error.status === 409 && error.problem?.detail
    ? { ...state, message: error.problem.detail }
    : state;
}

const RETRY_FIELDS = ["templateName", "email", "userId", "from", "to", "limit"] as const;

/**
 * POST /retry-failed for the journal's current template / email / user / dates (it has no status filter: only Failed
 * rows are retried). 202 means the rows were handed to the queue, not that they were delivered.
 */
export async function retryFailedAction(_state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, RETRY_FIELDS);
  const limit = values.limit === "" ? null : /^\d{1,3}$/.test(values.limit) ? Number(values.limit) : NaN;
  if (Number.isNaN(limit)) return { status: "error", message: "The limit is a whole number from 1 to 100.", values };
  try {
    const result = await retryFailedNotifications({
      templateName: values.templateName || null,
      email: values.email || null,
      userId: values.userId || null,
      from: startOfDayUtc(values.from) ?? null,
      to: endOfDayUtc(values.to) ?? null,
      limit,
    });
    revalidatePath(NOTIFICATIONS_PATH);
    const waiting = result.matching - result.dispatchedIds.length - result.failedIds.length;
    if (result.matching === 0) return { status: "ok", message: "No failed notification matches: nothing to retry." };
    return {
      status: result.failedIds.length > 0 ? "error" : "ok",
      message:
        `Handed ${result.dispatchedIds.length} of ${result.matching} failed notifications to delivery; refresh in a few seconds to see the outcome.` +
        (result.failedIds.length > 0 ? ` The message bus refused ${result.failedIds.length}.` : "") +
        (waiting > 0 ? ` ${waiting} are still waiting: press again.` : ""),
    };
  } catch (error) {
    return toFormState(error, { fields: [], values });
  }
}

/** POST /{id}/resend: the stored event goes back to the queue; the page shows the row as it is now. */
export async function resendAction(notificationId: string): Promise<FormState> {
  if (!GUID.test(notificationId)) return { status: "error", message: "Unknown notification." };
  try {
    await resendNotification(notificationId);
  } catch (error) {
    return withConflictDetail(error, toFormState(error, { fields: [] }));
  }
  revalidatePath(NOTIFICATIONS_PATH);
  redirect(`${NOTIFICATIONS_PATH}/${notificationId}?done=resent`);
}

/** POST /{id}/mark-undeliverable with the operator's reason; final from then on. */
export async function markUndeliverableAction(notificationId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["reason"] as const);
  if (!GUID.test(notificationId)) return { status: "error", message: "Unknown notification.", values };
  try {
    await markNotificationUndeliverable(notificationId, { reason: values.reason });
  } catch (error) {
    return withConflictDetail(error, toFormState(error, { fields: ["reason"], values }));
  }
  revalidatePath(NOTIFICATIONS_PATH);
  redirect(`${NOTIFICATIONS_PATH}/${notificationId}?done=undeliverable`);
}

/** POST /templates/{name}/test: synchronous; answers the mail server's Message-ID. No journal row. */
export async function testSendAction(templateName: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["email", "name"] as const);
  try {
    const result = await sendTestNotification(templateName, { email: values.email, name: values.name || null });
    return { status: "ok", message: `Sent. Message-ID ${result.providerMessageId}.` };
  } catch (error) {
    // 503 Notification.TestSendFailed: the mail server refused it or could not be reached.
    const state = toFormState(error, { fields: ["email", "name"], values });
    return error instanceof ApiError && error.status === 503 && error.problem?.detail
      ? { ...state, message: error.problem.detail }
      : state;
  }
}
