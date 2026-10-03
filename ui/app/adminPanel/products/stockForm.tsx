"use client";

import { useActionState } from "react";
import { errorId, FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type StockFormProps = {
  /** adjustStockAction bound to the product id. */
  action: (state: FormState, formData: FormData) => Promise<FormState>;
};

/** The only way the admin moves stock: the edit form has no stock field (catalog.md "Frontend notes"). */
function StockForm({ action }: StockFormProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const value = (name: string) => (state.status === "error" ? state.values?.[name] : undefined) ?? "";
  const invalid = (name: string) =>
    state.fieldErrors?.[name]?.length ? { "aria-invalid": true, "aria-describedby": errorId(name) } : {};

  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label="Adjust stock">
      <p className="text-xs text-muted-foreground">
        Fill one: a movement (a delivery is +5, a write-off -2) or the counted total from a stock-take.
      </p>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1.5">
          <Label htmlFor="delta">Change by</Label>
          <Input id="delta" name="delta" type="number" step="1" defaultValue={value("delta")} {...invalid("delta")} />
          <FieldError name="delta" state={state} />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="absolute">Set to</Label>
          <Input
            id="absolute"
            name="absolute"
            type="number"
            min="0"
            step="1"
            defaultValue={value("absolute")}
            {...invalid("absolute")}
          />
          <FieldError name="absolute" state={state} />
        </div>
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="reason">Reason (optional)</Label>
        <Input id="reason" name="reason" maxLength={500} defaultValue={value("reason")} {...invalid("reason")} />
        <FieldError name="reason" state={state} />
      </div>
      <FormMessage state={state} />
      <Button type="submit" variant="outline" disabled={pending}>
        {pending ? "Saving…" : "Update stock"}
      </Button>
    </form>
  );
}

export default StockForm;
