import { CircleAlert, CircleCheck } from "lucide-react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import type { FormState } from "@/lib/admin/forms";

/** The form-level result of an action: an error Alert (role="alert") or a short confirmation (role="status"). */
export function FormMessage({ state }: { state: FormState }) {
  if (!state.message) return null;
  if (state.status === "error") {
    return (
      <Alert variant="destructive" role="alert">
        <CircleAlert aria-hidden />
        <AlertDescription>{state.message}</AlertDescription>
      </Alert>
    );
  }
  return (
    <p role="status" className="flex items-center gap-1.5 text-sm text-emerald-700 dark:text-emerald-400">
      <CircleCheck aria-hidden className="size-4" />
      {state.message}
    </p>
  );
}

/** The id an input's aria-describedby points at when it has an error. */
export function errorId(name: string) {
  return `${name}-error`;
}

export function FieldError({ name, state }: { name: string; state: FormState }) {
  const messages = state.fieldErrors?.[name];
  if (!messages?.length) return null;
  return (
    <p id={errorId(name)} className="text-xs text-destructive">
      {messages.join(" ")}
    </p>
  );
}
