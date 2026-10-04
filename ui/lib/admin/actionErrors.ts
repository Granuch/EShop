import "server-only";

import { ApiError } from "@/lib/admin/api";
import { PermissionDeniedError } from "@/lib/admin/auth";
import type { FormState } from "@/lib/admin/forms";
import { ADMIN_ROLE_HINTS, type RoleGatedService } from "@/lib/admin/permissions";

interface FormErrorOptions {
  /** The form's input names; an `errors` key whose first segment matches one lands on that field. */
  fields: readonly string[];
  /** errorCodes that belong to one field, e.g. { "Product.SkuConflict": "sku" }. */
  codeFields?: Record<string, string>;
  /** For the 403 hint when the service still requires the Admin role. */
  service?: RoleGatedService;
  values?: Record<string, string>;
}

/**
 * Turns a failed write into form state (PLAN §2.4): ValidationError keys onto their fields, `$` and unmatched keys
 * to the form, 403/429/409 to fixed messages, anything else to `detail` plus the traceId for support.
 * Never throws, so an action always answers its form.
 */
export function toFormState(error: unknown, options: FormErrorOptions): FormState {
  const base: FormState = { status: "error", values: options.values };

  if (error instanceof PermissionDeniedError) return { ...base, message: error.message };
  if (!(error instanceof ApiError)) {
    return { ...base, message: "The API gateway could not be reached. Nothing was saved; try again." };
  }

  const problem = error.problem;
  switch (error.status) {
    case 401:
      return { ...base, message: "Your session has expired. Sign in again, then retry." };
    case 403: {
      const hint = options.service ? ` ${ADMIN_ROLE_HINTS[options.service]}` : "";
      return { ...base, message: `This account may not do this.${hint}` };
    }
    case 429:
      return { ...base, message: `Too many requests — try again in ${error.retryAfter ?? 60} s.` };
  }

  if (problem?.errorCode === "ValidationError" && problem.errors) {
    const fieldErrors: Record<string, string[]> = {};
    const formLevel: string[] = [];
    for (const [key, messages] of Object.entries(problem.errors)) {
      const field = key.split(/[.[]/, 1)[0];
      if (options.fields.includes(field)) (fieldErrors[field] ??= []).push(...messages);
      else formLevel.push(...messages);
    }
    return {
      ...base,
      fieldErrors,
      message: formLevel.length > 0 ? formLevel.join(" ") : "Check the highlighted fields.",
    };
  }

  const field = problem ? options.codeFields?.[problem.errorCode] : undefined;
  if (problem && field) {
    return { ...base, fieldErrors: { [field]: [problem.detail ?? problem.errorCode] }, message: "Check the highlighted fields." };
  }

  if (error.status === 409) {
    return {
      ...base,
      message: `${problem?.detail ?? "Someone else changed this at the same time."} Reload the page and try again.`,
    };
  }

  if (error.status >= 500 || !problem) {
    const reference = problem?.traceId ? ` Reference: ${problem.traceId}` : "";
    return {
      ...base,
      message: `The service could not complete the request (HTTP ${error.status}). Try again in a moment.${reference}`,
    };
  }

  return { ...base, message: problem.detail ?? `The request was refused (${problem.errorCode}).` };
}
