"use client";

import { useActionState, useId, useState } from "react";
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
import { IDLE, type FormState } from "@/lib/admin/forms";

const COPY = {
  ship: {
    button: "Mark as shipped",
    title: "Mark this order as shipped?",
    description: "The customer is emailed that the order has shipped. This cannot be undone.",
  },
  deliver: {
    button: "Mark as delivered",
    title: "Mark this order as delivered?",
    description: "This cannot be undone.",
  },
} as const;

type TransitionButtonProps = {
  /** null once the order has no admin transition left: only the last result is shown, so "Marked as delivered" stays. */
  transition: "ship" | "deliver" | null;
  orderLabel: string;
  /** transitionOrderAction bound to the order id and the transition. */
  action: () => Promise<FormState>;
};

/**
 * Confirmed with an AlertDialog because the transition is irreversible and (for ship) emails the customer. The form
 * sits outside the dialog's portal and the confirm button submits it through the `form` attribute, so closing the
 * dialog cannot unmount the form mid-submit.
 */
function TransitionButton({ transition, orderLabel, action }: TransitionButtonProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const [open, setOpen] = useState(false);
  const formId = useId();
  const copy = transition ? COPY[transition] : null;

  return (
    <div className="flex flex-col items-end gap-1">
      {copy && (
        <>
          <form id={formId} action={formAction} />
          <AlertDialog open={open} onOpenChange={setOpen}>
            <AlertDialogTrigger render={<Button disabled={pending} />}>
              {pending ? "Saving…" : copy.button}
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{copy.title}</AlertDialogTitle>
                <AlertDialogDescription>
                  Order {orderLabel}. {copy.description}
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancel</AlertDialogCancel>
                <AlertDialogAction type="submit" form={formId} onClick={() => setOpen(false)}>
                  {copy.button}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </>
      )}
      {state.message && (
        <p
          role={state.status === "error" ? "alert" : "status"}
          className={`max-w-xs text-right text-xs ${state.status === "error" ? "text-destructive" : "text-emerald-700 dark:text-emerald-400"}`}
        >
          {state.message}
        </p>
      )}
    </div>
  );
}

export default TransitionButton;
