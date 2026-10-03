"use client";

import { useActionState } from "react";
import { errorId, FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type NoteFormProps = {
  /** addOrderNoteAction bound to the order id. */
  action: (state: FormState, formData: FormData) => Promise<FormState>;
};

function NoteForm({ action }: NoteFormProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const invalid = state.fieldErrors?.body?.length ? { "aria-invalid": true, "aria-describedby": errorId("body") } : {};

  return (
    <form key={formKey(state)} action={formAction} className="space-y-2" aria-label="Add a note">
      <Label htmlFor="body">New note</Label>
      <Textarea
        id="body"
        name="body"
        rows={3}
        maxLength={2000}
        defaultValue={state.status === "error" ? state.values?.body : undefined}
        {...invalid}
      />
      <p className="text-xs text-muted-foreground">Internal: never shown to the customer. Notes cannot be edited.</p>
      <FieldError name="body" state={state} />
      <FormMessage state={state} />
      <Button type="submit" variant="outline" disabled={pending}>
        {pending ? "Saving…" : "Add note"}
      </Button>
    </form>
  );
}

export default NoteForm;
