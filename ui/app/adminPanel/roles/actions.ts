"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { toFormState } from "@/lib/admin/actionErrors";
import { getAdminSession } from "@/lib/admin/auth";
import { readFields, type FormState } from "@/lib/admin/forms";
import { addRoleUser, createRole, deleteRole, listAdminUsers, removeRoleUser, updateRole } from "@/lib/admin/identity";

// Role writes: roles.manage here, the Admin ROLE at Identity (hence the "identity" 403 hint).

const ROLES_PATH = "/adminPanel/roles";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ROLE_NAME = /^[A-Za-z0-9 _-]{1,256}$/;

function revalidateRoles(roleId?: string) {
  revalidatePath(ROLES_PATH);
  if (roleId) revalidatePath(`${ROLES_PATH}/${roleId}`);
  revalidatePath("/adminPanel/users", "layout");
}

/** POST /roles. A taken name (any case) is 400 Role.Exists, shown on the name field. */
export async function createRoleAction(_state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["name", "description"] as const);
  if (!ROLE_NAME.test(values.name)) {
    return {
      status: "error",
      message: "Check the highlighted fields.",
      fieldErrors: { name: ["Use letters, digits, spaces, _ and -."] },
      values,
    };
  }
  try {
    await createRole({ name: values.name, description: values.description || null });
  } catch (error) {
    return toFormState(error, {
      fields: ["name", "description"],
      codeFields: { "Role.Exists": "name" },
      service: "identity",
      values,
    });
  }
  revalidateRoles();
  return { status: "ok", message: `Role "${values.name}" created. It grants no permission: only Admin has a bundle.` };
}

/** PUT /roles/{id}: replaces the description; an empty field clears it. */
export async function updateRoleAction(roleId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["description"] as const);
  if (!GUID.test(roleId)) return { status: "error", message: "Unknown role.", values };
  try {
    await updateRole(roleId, { description: values.description || null });
  } catch (error) {
    return toFormState(error, { fields: ["description"], service: "identity", values });
  }
  revalidateRoles(roleId);
  return { status: "ok", message: values.description ? "Description saved." : "Description cleared." };
}

/** DELETE /roles/{id}: members lose the role; Admin and User are refused (400 Role.CannotDelete). */
export async function deleteRoleAction(roleId: string): Promise<FormState> {
  if (!GUID.test(roleId)) return { status: "error", message: "Unknown role." };
  try {
    await deleteRole(roleId);
  } catch (error) {
    return toFormState(error, { fields: [], service: "identity" });
  }
  revalidateRoles(roleId);
  redirect(`${ROLES_PATH}?deleted=1`);
}

/** POST /roles/{name}/users/{userId}, the user found by exact email (the list search is a substring match). */
export async function addMemberAction(roleId: string, roleName: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["email"] as const);
  if (!GUID.test(roleId)) return { status: "error", message: "Unknown role.", values };
  try {
    const matches = await listAdminUsers({ search: values.email, pageSize: 100 });
    const user = matches.items.find((item) => item.email?.toLowerCase() === values.email.toLowerCase());
    if (!user) {
      return {
        status: "error",
        message: "Check the highlighted fields.",
        fieldErrors: { email: ["No live account has this email."] },
        values,
      };
    }
    await addRoleUser(roleName, user.id);
  } catch (error) {
    // 400 Role.AddUserFailed: "User already in role '…'."
    return toFormState(error, { fields: ["email"], codeFields: { "Role.AddUserFailed": "email" }, service: "identity", values });
  }
  revalidateRoles(roleId);
  return { status: "ok", message: `${values.email} added.` };
}

/** DELETE /roles/{name}/users/{userId}. Removing yourself from Admin is refused here: it would lock you out. */
export async function removeMemberAction(roleId: string, roleName: string, userId: string): Promise<FormState> {
  if (!GUID.test(roleId)) return { status: "error", message: "Unknown role." };
  const session = await getAdminSession();
  if (roleName.toLowerCase() === "admin" && session.id === userId) {
    return { status: "error", message: "You cannot remove yourself from Admin: you would lose this panel." };
  }
  try {
    await removeRoleUser(roleName, userId);
  } catch (error) {
    return toFormState(error, { fields: [], service: "identity" });
  }
  revalidateRoles(roleId);
  return { status: "ok", message: "Removed." };
}
