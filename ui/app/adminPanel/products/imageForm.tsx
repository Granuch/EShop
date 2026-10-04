"use client";

import { useActionState, useId } from "react";
import { FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type ImageFormProps = {
  /** addImageAction or updateImageAction, bound to the product (and image) id. */
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  initial?: { url: string; altText: string };
  submitLabel: string;
  ariaLabel: string;
  /** Edit: the API replaces both fields, so an emptied alt text clears it. */
  note?: string;
};

/** URL + alt text, for adding an image or replacing one. Several sit on one page, so the input ids are generated. */
function ImageForm({ action, initial, submitLabel, ariaLabel, note }: ImageFormProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const id = useId();
  const value = (name: "url" | "altText") =>
    state.status === "error" ? (state.values?.[name] ?? "") : state.status === "ok" && !initial ? "" : (initial?.[name] ?? "");
  const invalid = (name: string) =>
    state.fieldErrors?.[name]?.length ? { "aria-invalid": true, "aria-describedby": `${name}-error` } : {};

  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label={ariaLabel}>
      <div className="space-y-1.5">
        <Label htmlFor={`${id}-url`}>Image URL</Label>
        <Input
          id={`${id}-url`}
          name="url"
          type="url"
          required
          maxLength={500}
          placeholder="https://…"
          defaultValue={value("url")}
          {...invalid("url")}
        />
        <FieldError name="url" state={state} />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor={`${id}-alt`}>Alt text (optional)</Label>
        <Input id={`${id}-alt`} name="altText" maxLength={200} defaultValue={value("altText")} {...invalid("altText")} />
        <FieldError name="altText" state={state} />
      </div>
      {note && <p className="text-xs text-muted-foreground">{note}</p>}
      <FormMessage state={state} />
      <Button type="submit" variant="outline" size="sm" disabled={pending}>
        {pending ? "Saving…" : submitLabel}
      </Button>
    </form>
  );
}

export default ImageForm;
