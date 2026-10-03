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
