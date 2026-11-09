"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { toFormState } from "@/lib/admin/actionErrors";
import { ApiError } from "@/lib/admin/api";
import { readFields, type FormState } from "@/lib/admin/forms";
import {
  accountOperation,
  changeAdminUserEmail,
  createAdminUser,
  deleteAdminUser,
  lockAdminUser,
  setAdminUserRoles,
  updateAdminUser,
  type AccountOperation,
} from "@/lib/admin/identity";
import { isDoneNotice } from "./notices";

// Server actions for admin users. Each re-checks its permission inside the DAL call and answers its form.

const USERS_PATH = "/adminPanel/users";

function revalidateUser(userId: string) {
  revalidatePath(USERS_PATH);
  revalidatePath(`${USERS_PATH}/${userId}`);
}

/** Identity ids are strings; only the characters a GUID uses are let through to a path. */
const ID = /^[0-9A-Za-z-]{1,64}$/;

function roleNames(formData: FormData): string[] {
  return [...new Set(formData.getAll("roles").filter((role): role is string => typeof role === "string" && role.trim() !== ""))];
}

const CREATE_FIELDS = ["email", "firstName", "lastName", "phoneNumber", "password"] as const;

/**
 * POST /admin/users. No password → an invite: the account has none and the user is emailed a set-password link. The
 * role picker always sends its set when shown (none ticked = no role); without it, roles are omitted → ["User"].
 */
export async function createUserAction(_state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, CREATE_FIELDS);
  const rolesShown = formData.get("rolesShown") === "1";
  let userId: string;
  let inviteSent: boolean;
  try {
    ({ userId, inviteSent } = await createAdminUser({
      email: values.email,
      firstName: values.firstName,
      lastName: values.lastName,
      phoneNumber: values.phoneNumber || null,
      // Not trimmed: a password may begin or end with a space.
      password: (formData.get("password") as string | null) || null,
      ...(rolesShown ? { roles: roleNames(formData) } : {}),
      emailConfirmed: formData.get("emailConfirmed") === "on",
    }));
  } catch (error) {
    return toFormState(error, {
      fields: CREATE_FIELDS,
      codeFields: { "User.EmailConflict": "email", "User.CreateFailed": "password" },
      values: { ...values, password: "" },
    });
  }
  revalidatePath(USERS_PATH);
  redirect(`${USERS_PATH}/${userId}${inviteSent ? "?invited=1" : "?created=1"}`);
}

const PROFILE_FIELDS = ["firstName", "lastName", "phoneNumber", "profilePictureUrl"] as const;

/** PUT /{id}: every field is sent; "" clears the phone number and the picture, as the form says. */
export async function updateUserAction(userId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, PROFILE_FIELDS);
  if (!ID.test(userId)) return { status: "error", message: "Unknown user.", values };
  try {
    await updateAdminUser(userId, values);
  } catch (error) {
    return toFormState(error, { fields: PROFILE_FIELDS, values });
  }
  revalidateUser(userId);
  return { status: "ok", message: "Profile saved." };
}

/** PUT /{id}/email: the box is the API's markConfirmed; unticked unconfirms it and signs the user out everywhere. */
export async function changeEmailAction(userId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["email"] as const);
  if (!ID.test(userId)) return { status: "error", message: "Unknown user.", values };
  const markConfirmed = formData.get("markConfirmed") === "on";
  try {
    await changeAdminUserEmail(userId, { email: values.email, markConfirmed });
  } catch (error) {
    return toFormState(error, { fields: ["email"], codeFields: { "User.EmailConflict": "email" }, values });
  }
  revalidateUser(userId);
  return { status: "ok", message: markConfirmed ? "Email changed, confirmed." : "Email changed; it is unconfirmed now." };
}

/** PUT /{id}/roles with the ticked set; none ticked removes every role. */
export async function setRolesAction(userId: string, _state: FormState, formData: FormData): Promise<FormState> {
  if (!ID.test(userId)) return { status: "error", message: "Unknown user." };
  const roles = roleNames(formData);
  try {
    await setAdminUserRoles(userId, { roles });
  } catch (error) {
    return toFormState(error, { fields: [] });
  }
  revalidateUser(userId);
  return { status: "ok", message: roles.length ? `Roles saved: ${roles.join(", ")}.` : "Every role removed." };
}

/** Messages of the operations whose button stays in place; the others redirect with a notice (notices.ts). */
const DONE: Partial<Record<AccountOperation, string>> = {
  "reset-password": "A password-reset email was sent.",
  "revoke-tokens": "Signed out everywhere. Access tokens already issued work until they expire (at most an hour).",
};

/** The body-less account writes. 409 User.NotDeleted and 400 User.2FANotEnabled carry their own detail. */
export async function accountAction(userId: string, operation: AccountOperation): Promise<FormState> {
  if (!ID.test(userId)) return { status: "error", message: "Unknown user." };
  try {
    await accountOperation(userId, operation);
  } catch (error) {
    const state = toFormState(error, { fields: [] });
    return error instanceof ApiError && error.status === 409 && error.problem?.detail
      ? { ...state, message: error.problem.detail }
      : state;
  }
  revalidateUser(userId);
  if (isDoneNotice(operation)) redirect(`${USERS_PATH}/${userId}?done=${operation}`);
  return { status: "ok", message: DONE[operation] };
}

/**
 * POST /{id}/lock. The form's date and time are UTC (as every admin date is), sent with Z. A past moment is the API's
 * to refuse ("a past date is an unlock, not a lock").
 */
export async function lockUserAction(userId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["until", "reason"] as const);
  if (!ID.test(userId)) return { status: "error", message: "Unknown user.", values };
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(values.until)) {
    return { status: "error", message: "Check the highlighted fields.", fieldErrors: { until: ["Choose a date and time."] }, values };
  }
  try {
    await lockAdminUser(userId, { until: `${values.until}:00Z`, reason: values.reason || null });
  } catch (error) {
    return toFormState(error, { fields: ["until", "reason"], values });
  }
  revalidateUser(userId);
  redirect(`${USERS_PATH}/${userId}?done=lock`);
}

/** DELETE /{id}: soft; the user stays readable here, marked deleted, and can be restored. */
export async function deleteUserAction(userId: string): Promise<FormState> {
  if (!ID.test(userId)) return { status: "error", message: "Unknown user." };
  try {
    await deleteAdminUser(userId);
  } catch (error) {
    return toFormState(error, { fields: [] });
  }
  revalidateUser(userId);
  redirect(`${USERS_PATH}/${userId}?done=delete`);
}
