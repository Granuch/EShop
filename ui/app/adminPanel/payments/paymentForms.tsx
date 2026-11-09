"use client";

import { useActionState, useId, useState } from "react";
import { FieldError, FormMessage } from "@/components/Admin/formMessage";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type Action = (state: FormState, formData: FormData) => Promise<FormState>;

/** Records the payment as received outside the system; the reference is the operator's evidence, unique. */
export function OfflineSettleForm({ action }: { action: Action }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const id = useId();
  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label="Settle offline">
      <div className="space-y-1.5">
        <Label htmlFor={`${id}-reference`}>Reference</Label>
        <Input
          id={`${id}-reference`}
          name="reference"
          required
          maxLength={100}
          placeholder="Transfer reference or receipt number"
          defaultValue={state.status === "error" ? (state.values?.reference ?? "") : ""}
          aria-invalid={state.fieldErrors?.reference?.length ? true : undefined}
          aria-describedby={state.fieldErrors?.reference?.length ? "reference-error" : undefined}
        />
        <p className="text-xs text-muted-foreground">Settles the recorded amount. The order becomes Paid within seconds.</p>
        <FieldError name="reference" state={state} />
      </div>
      <FormMessage state={state} />
      <Button type="submit" variant="outline" disabled={pending}>
        {pending ? "Settling…" : "Mark as paid offline"}
      </Button>
    </form>
  );
}

/** A full refund, confirmed first. The confirm button submits the form from outside the dialog's portal. */
export function RefundForm({ action, amountLabel, offline }: { action: Action; amountLabel: string; offline: boolean }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const [open, setOpen] = useState(false);
  const formId = useId();
  return (
    <form key={formKey(state)} id={formId} action={formAction} className="space-y-3" aria-label="Refund">
      <div className="space-y-1.5">
        <Label htmlFor={`${formId}-reason`}>Reason (optional)</Label>
        <Input
          id={`${formId}-reason`}
          name="reason"
          maxLength={500}
          defaultValue={state.status === "error" ? (state.values?.reason ?? "") : ""}
        />
        <p className="text-xs text-muted-foreground">Stored on the payment and its timeline.</p>
        <FieldError name="reason" state={state} />
      </div>
      <FormMessage state={state} />
      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogTrigger render={<Button type="button" variant="destructive" disabled={pending} />}>
          {pending ? "Refunding…" : `Refund ${amountLabel}`}
        </AlertDialogTrigger>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{`Refund ${amountLabel} in full?`}</AlertDialogTitle>
            <AlertDialogDescription>
              {offline
                ? "This payment was received outside the system: it is only recorded as refunded, and the money must be paid back outside the system too."
                : "The card is refunded through Stripe."}{" "}
              The order becomes Refunded and the customer is emailed. This cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction type="submit" form={formId} variant="destructive" onClick={() => setOpen(false)}>
              Refund
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </form>
  );
}
