// The state a server action returns to a `useActionState` form. Plain data only: it is serialised to the client.

export interface FormState {
  status: "idle" | "ok" | "error";
  /** ok: a confirmation; error: the form-level message (detail, a `$` rule, a 403/429 hint). */
  message?: string;
  /** Keyed by the input's name, which is the field's camelCase wire name (conventions §3.3). */
  fieldErrors?: Record<string, string[]>;
  /** What was submitted, so the form can show it again after React resets the fields. */
  values?: Record<string, string>;
}

export const IDLE: FormState = { status: "idle" };

/** Reads the named fields as trimmed strings ("" when absent). */
export function readFields<K extends string>(formData: FormData, names: readonly K[]): Record<K, string> {
  const out = {} as Record<K, string>;
  for (const name of names) {
    const value = formData.get(name);
    out[name] = typeof value === "string" ? value.trim() : "";
  }
  return out;
}

const formKeys = new WeakMap<FormState, number>();
let lastFormKey = 0;

/**
 * A key that changes with every action result. Put it on the <form> so the fields remount with their new
 * defaultValue: Base UI's Input (a FieldControl) logs an error when an uncontrolled field's defaultValue changes after
 * it was initialised, which is exactly what echoing `values` back, or re-rendering with fresh data, does.
 */
export function formKey(state: FormState): number {
  let key = formKeys.get(state);
  if (key === undefined) {
    key = ++lastFormKey;
    formKeys.set(state, key);
  }
  return key;
}
