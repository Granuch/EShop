"use client";

import { useActionState } from "react";
import { errorId, FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type DiscountFormProps = {
  /** setDiscountAction bound to the product id. */
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  /** The current discount, to prefill the field. */
  current: number | null;
};

/** Sets the promotional price; it must be strictly below the list price (the API says so if it is not). */
function DiscountForm({ action, current }: DiscountFormProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const value = state.status === "error" ? (state.values?.discountPrice ?? "") : current === null ? "" : String(current);
  const invalid = state.fieldErrors?.discountPrice?.length
    ? { "aria-invalid": true, "aria-describedby": errorId("discountPrice") }
    : {};

  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label="Set discount">
      <div className="space-y-1.5">
        <Label htmlFor="discountPrice">Discount price</Label>
        <Input
          id="discountPrice"
          name="discountPrice"
          type="number"
          min="0.01"
          step="0.01"
          defaultValue={value}
          {...invalid}
        />
        <FieldError name="discountPrice" state={state} />
      </div>
      <FormMessage state={state} />
      <Button type="submit" variant="outline" disabled={pending}>
        {pending ? "Saving…" : current === null ? "Set discount" : "Change discount"}
      </Button>
    </form>
  );
}

export default DiscountForm;
