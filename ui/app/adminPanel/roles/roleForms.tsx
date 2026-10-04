"use client";

import { useActionState, useId } from "react";
import { FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type Action = (state: FormState, formData: FormData) => Promise<FormState>;

type FieldSpec = { name: string; label: string; initial?: string; hint?: string } & React.ComponentProps<"input">;

/**
 * A small form of plain text fields with one submit button. A successful create clears the fields; an edit keeps
 * the saved values (the page re-renders them as `initial`).
 */
function SimpleForm({ action, fields, submit, ariaLabel, clearOnSuccess }: {
  action: Action;
  fields: FieldSpec[];
  submit: string;
  ariaLabel: string;
  clearOnSuccess?: boolean;
}) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const id = useId();
  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label={ariaLabel}>
      {fields.map(({ name, label, initial, hint, ...input }) => (
        <div key={name} className="space-y-1.5">
          <Label htmlFor={`${id}-${name}`}>{label}</Label>
          <Input
            id={`${id}-${name}`}
            name={name}
            defaultValue={
              state.status === "error"
                ? (state.values?.[name] ?? "")
                : state.status === "ok" && clearOnSuccess
                  ? ""
                  : (initial ?? "")
            }
            aria-invalid={state.fieldErrors?.[name]?.length ? true : undefined}
            aria-describedby={state.fieldErrors?.[name]?.length ? `${name}-error` : undefined}
            {...input}
          />
          {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
          <FieldError name={name} state={state} />
        </div>
      ))}
      <FormMessage state={state} />
      <Button type="submit" variant="outline" disabled={pending}>
        {pending ? "Saving…" : submit}
      </Button>
    </form>
  );
}

export function CreateRoleForm({ action }: { action: Action }) {
  return (
    <SimpleForm
      action={action}
      ariaLabel="New role"
      submit="Create role"
      clearOnSuccess
      fields={[
        { name: "name", label: "Name", required: true, maxLength: 256, hint: "Letters, digits, spaces, _ and -. It cannot be renamed later." },
        { name: "description", label: "Description (optional)", maxLength: 250 },
      ]}
    />
  );
}

export function DescriptionForm({ action, description }: { action: Action; description: string }) {
  return (
    <SimpleForm
      action={action}
      ariaLabel="Edit description"
      submit="Save description"
      fields={[{ name: "description", label: "Description", maxLength: 250, initial: description, hint: "Empty clears it." }]}
    />
  );
}

export function AddMemberForm({ action }: { action: Action }) {
  return (
    <SimpleForm
      action={action}
      ariaLabel="Add member"
      submit="Add member"
      clearOnSuccess
      fields={[{ name: "email", label: "User's email", type: "email", required: true, maxLength: 256 }]}
    />
  );
}
