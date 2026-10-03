"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { toFormState } from "@/lib/admin/actionErrors";
import { ApiError } from "@/lib/admin/api";
import {
  createCategory,
  deleteCategory,
  getCategoryTree,
  moveCategory,
  reorderCategories,
  restoreCategory,
  updateCategory,
} from "@/lib/admin/catalog";
import { readFields, type FormState } from "@/lib/admin/forms";
import type { Category } from "@/lib/admin/types/catalog";

// Server actions for categories. Each re-checks the permission inside its DAL call and answers its form.

const CATEGORIES_PATH = "/adminPanel/categories";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const SLUG_MESSAGE = "Use lower-case letters, digits and single hyphens, e.g. kids-shoes.";

/** Category names appear in the admin's product pages and the storefront's filters too. */
function revalidateCategories() {
  revalidatePath(CATEGORIES_PATH, "layout");
  revalidatePath("/adminPanel/products", "layout");
  revalidatePath("/", "layout");
}

/** The API's 409/400 details here name the fix ("Restore the parent first…"), so they are shown as they are. */
function withDetail(error: unknown, state: FormState): FormState {
  if (error instanceof ApiError && error.problem?.detail && error.status === 409) {
    return { ...state, message: error.problem.detail };
  }
  return state;
}

function findLevel(tree: Category[], parentId: string | null): Category[] | null {
  if (parentId === null) return tree;
  for (const node of tree) {
    if (node.id === parentId) return node.childCategories;
    const found = findLevel(node.childCategories, parentId);
    if (found) return found;
  }
  return null;
}

/** The parent's name from the admin tree (which lists deleted categories), or null if it cannot be read. */
async function parentName(categoryId: string): Promise<string | null> {
  try {
    const walk = (nodes: Category[], parent: Category | null): Category | null | undefined => {
      for (const node of nodes) {
        if (node.id === categoryId) return parent;
        const found = walk(node.childCategories, node);
        if (found !== undefined) return found;
      }
      return undefined;
    };
    return walk(await getCategoryTree(), null)?.name ?? null;
  } catch {
    return null;
  }
}

/**
 * Moves a category one place up (-1) or down (+1) among its live siblings. Reorder takes every live category of the
 * level exactly once, so the level is re-read here (the tree comes sorted by displayOrder, then name).
 */
export async function moveCategoryOrderAction(parentId: string | null, categoryId: string, offset: -1 | 1): Promise<FormState> {
  if (!GUID.test(categoryId) || (parentId !== null && !GUID.test(parentId))) return { status: "error", message: "Unknown category." };
  try {
    const level = findLevel(await getCategoryTree(), parentId);
    const ids = (level ?? []).filter((node) => node.isActive).map((node) => node.id);
    const from = ids.indexOf(categoryId);
    if (from < 0) return { status: "error", message: "This category changed meanwhile. Reload the page." };
    const to = from + offset;
    if (to < 0 || to >= ids.length) return { status: "ok" };
    [ids[from], ids[to]] = [ids[to], ids[from]];
    await reorderCategories({ parentCategoryId: parentId, categoryIds: ids });
  } catch (error) {
    return toFormState(error, { fields: [], service: "catalog" });
  }
  revalidateCategories();
  return { status: "ok" };
}

/** Restore: back at its old place. A deleted parent (400) and a taken slug (409) carry the fix in `detail`. */
export async function restoreCategoryAction(categoryId: string): Promise<FormState> {
  if (!GUID.test(categoryId)) return { status: "error", message: "Unknown category." };
  try {
    await restoreCategory(categoryId);
  } catch (error) {
    // The API's detail names the parent by id and quotes an API path; name it instead.
    if (error instanceof ApiError && error.problem?.errorCode === "Category.ParentNotActive") {
      const parent = await parentName(categoryId);
      return {
        status: "error",
        message: `Its parent category${parent ? ` "${parent}"` : ""} is deleted. Restore the parent first, then this one.`,
      };
    }
    return withDetail(error, toFormState(error, { fields: [], service: "catalog" }));
  }
  revalidateCategories();
  return { status: "ok", message: "Restored." };
}

const FIELDS = ["name", "slug", "description", "displayOrder", "parentCategoryId"] as const;

function parseOrder(value: string): number | null | undefined {
  if (value === "") return undefined;
  return /^\d{1,9}$/.test(value) ? Number(value) : null;
}

/** POST /categories. Every refusal is a 400 here, the slug conflict included (it lands on the slug field). */
export async function createCategoryAction(_state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, FIELDS);
  const fieldErrors: Record<string, string[]> = {};
  const displayOrder = parseOrder(values.displayOrder);
  if (displayOrder === null) fieldErrors.displayOrder = ["Enter a whole number, 0 or more."];
  if (values.slug && !SLUG.test(values.slug)) fieldErrors.slug = [SLUG_MESSAGE];
  if (values.parentCategoryId && !GUID.test(values.parentCategoryId)) fieldErrors.parentCategoryId = ["Choose a parent."];
  if (Object.keys(fieldErrors).length > 0) return { status: "error", message: "Check the highlighted fields.", fieldErrors, values };

  let id: string;
  try {
    ({ id } = await createCategory({
      name: values.name,
      ...(values.slug ? { slug: values.slug } : {}),
      parentCategoryId: values.parentCategoryId || null,
      ...(values.description ? { description: values.description } : {}),
      ...(displayOrder !== undefined ? { displayOrder } : {}),
    }));
  } catch (error) {
    return toFormState(error, {
      fields: FIELDS,
      codeFields: { "Category.SlugConflict": "slug", "Category.ParentNotFound": "parentCategoryId" },
      service: "catalog",
      values,
    });
  }
  revalidateCategories();
  redirect(`${CATEGORIES_PATH}/${id}`);
}

const EDIT_FIELDS = ["name", "slug", "description", "displayOrder"] as const;

/** PUT /categories/{id}: name, slug, description and order; the parent moves through moveCategoryParentAction. */
export async function updateCategoryAction(categoryId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, EDIT_FIELDS);
  if (!GUID.test(categoryId)) return { status: "error", message: "Unknown category.", values };
  const fieldErrors: Record<string, string[]> = {};
  const displayOrder = parseOrder(values.displayOrder);
  if (displayOrder === null) fieldErrors.displayOrder = ["Enter a whole number, 0 or more."];
  // "" would be a 400 at the API (a category always has a slug): say so here.
  if (!values.slug) fieldErrors.slug = ["A category needs a slug."];
  else if (!SLUG.test(values.slug)) fieldErrors.slug = [SLUG_MESSAGE];
  if (Object.keys(fieldErrors).length > 0) return { status: "error", message: "Check the highlighted fields.", fieldErrors, values };

  try {
    await updateCategory(categoryId, {
      id: categoryId,
      name: values.name,
      slug: values.slug,
      // "" clears the description, as the form says.
      description: values.description,
      ...(displayOrder !== undefined ? { displayOrder } : {}),
    });
  } catch (error) {
    return toFormState(error, { fields: EDIT_FIELDS, codeFields: { "Category.SlugConflict": "slug" }, service: "catalog", values });
  }
  revalidateCategories();
  return { status: "ok", message: "Changes saved." };
}

/** PUT /categories/{id}/parent; "" in the form means the root, sent as an explicit null. */
export async function moveCategoryParentAction(categoryId: string, _state: FormState, formData: FormData): Promise<FormState> {
  const values = readFields(formData, ["newParentCategoryId"] as const);
  if (!GUID.test(categoryId)) return { status: "error", message: "Unknown category.", values };
  const parent = values.newParentCategoryId;
  if (parent && !GUID.test(parent)) return { status: "error", message: "Choose a parent.", values };
  try {
    await moveCategory(categoryId, { newParentCategoryId: parent || null });
  } catch (error) {
    return withDetail(error, toFormState(error, { fields: ["newParentCategoryId"], service: "catalog", values }));
  }
  revalidateCategories();
  return { status: "ok", message: "Moved." };
}

/** DELETE: only an empty category. 409 HasChildren / HasProducts carry the reason in `detail`. */
export async function deleteCategoryAction(categoryId: string): Promise<FormState> {
  if (!GUID.test(categoryId)) return { status: "error", message: "Unknown category." };
  try {
    await deleteCategory(categoryId);
  } catch (error) {
    return withDetail(error, toFormState(error, { fields: [], service: "catalog" }));
  }
  revalidateCategories();
  redirect(CATEGORIES_PATH);
}
