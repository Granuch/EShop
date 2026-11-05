"use client";

import { useActionState, useId } from "react";
import { FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type Action = (state: FormState, formData: FormData) => Promise<FormState>;

/** One attribute: add (empty, cleared after success) or edit (prefilled; the API replaces both fields). */
export function AttributeForm({
  action,
  initial,
  submitLabel,
  ariaLabel,
}: {
  action: Action;
  initial?: { name: string; value: string };
  submitLabel: string;
  ariaLabel: string;
}) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const id = useId();
  const value = (name: "name" | "value") =>
    state.status === "error" ? (state.values?.[name] ?? "") : state.status === "ok" && !initial ? "" : (initial?.[name] ?? "");
  const invalid = (name: string) => (state.fieldErrors?.[name]?.length ? { "aria-invalid": true } : {});

  return (
    <form key={formKey(state)} action={formAction} className="space-y-2" aria-label={ariaLabel}>
      <div className="grid grid-cols-2 gap-2">
        <div className="space-y-1">
          <Label htmlFor={`${id}-name`}>Name</Label>
          <Input id={`${id}-name`} name="name" required maxLength={100} defaultValue={value("name")} {...invalid("name")} />
          <FieldError name="name" state={state} />
        </div>
        <div className="space-y-1">
          <Label htmlFor={`${id}-value`}>Value</Label>
          <Input
            id={`${id}-value`}
            name="value"
            required
            maxLength={200}
            defaultValue={value("value")}
            {...invalid("value")}
          />
          <FieldError name="value" state={state} />
        </div>
      </div>
      <FormMessage state={state} />
      <Button type="submit" variant="outline" size="sm" disabled={pending}>
        {pending ? "Saving…" : submitLabel}
      </Button>
    </form>
  );
}

/** The whole set as "Name: Value" lines, sent to the replace-all endpoint. An empty box removes every attribute. */
export function AttributesTextForm({ action, initial }: { action: Action; initial: string }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const value = state.status === "error" ? (state.values?.attributes ?? "") : initial;

  return (
    <form key={formKey(state)} action={formAction} className="space-y-2" aria-label="Edit all attributes">
      <Label htmlFor="attributes">One per line, as Name: Value</Label>
      <Textarea
        id="attributes"
        name="attributes"
        rows={Math.min(Math.max(initial.split("\n").length + 1, 4), 12)}
        defaultValue={value}
        aria-invalid={state.fieldErrors?.attributes?.length ? true : undefined}
        className="font-mono text-xs"
      />
      <FieldError name="attributes" state={state} />
      <p className="text-xs text-muted-foreground">
        Names already on the product keep their identity; missing names are removed. Leave it empty to remove all.
      </p>
      <FormMessage state={state} />
      <Button type="submit" variant="outline" size="sm" disabled={pending}>
        {pending ? "Saving…" : "Replace all attributes"}
      </Button>
    </form>
  );
}
