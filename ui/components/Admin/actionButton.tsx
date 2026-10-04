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
import { cn } from "@/lib/utils";

type ActionButtonProps = {
  /** A server action bound to everything it needs; it answers with form state (or redirects). */
  action: () => Promise<FormState>;
  children: React.ReactNode;
  /** Accessible name when the visible content is only an icon. */
  label?: string;
  variant?: "default" | "outline" | "destructive" | "ghost" | "secondary";
  size?: "default" | "sm" | "icon-sm";
  /** Ask first: irreversible or destructive writes. */
  confirm?: { title: string; description: string; confirmLabel: string };
  /** Where the result message goes relative to the button. */
  align?: "start" | "end";
  disabled?: boolean;
};

/**
 * One button, one server action, its result shown beside it. With `confirm`, an AlertDialog asks first; the form sits
 * outside the dialog's portal and the confirm button submits it through the `form` attribute, so closing the dialog
 * cannot unmount the form mid-submit.
 */
function ActionButton({
  action,
  children,
  label,
  variant = "outline",
  size = "sm",
  confirm,
  align = "end",
  disabled,
}: ActionButtonProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const [open, setOpen] = useState(false);
  const formId = useId();
  const button = { variant, size, disabled: disabled || pending, "aria-label": label } as const;

  return (
    <div className={cn("flex flex-col gap-1", align === "end" ? "items-end" : "items-start")}>
      <form id={formId} action={formAction} />
      {confirm ? (
        <AlertDialog open={open} onOpenChange={setOpen}>
          <AlertDialogTrigger render={<Button {...button} />}>{pending ? "Saving…" : children}</AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{confirm.title}</AlertDialogTitle>
              <AlertDialogDescription>{confirm.description}</AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Cancel</AlertDialogCancel>
              <AlertDialogAction
                type="submit"
                form={formId}
                variant={variant === "destructive" ? "destructive" : "default"}
                onClick={() => setOpen(false)}
              >
                {confirm.confirmLabel}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      ) : (
        <Button type="submit" form={formId} {...button}>
          {pending && size !== "icon-sm" ? "Saving…" : children}
        </Button>
      )}
      {state.message && (
        <p
          role={state.status === "error" ? "alert" : "status"}
          className={cn(
            "max-w-xs text-xs",
            align === "end" ? "text-right" : "text-left",
            state.status === "error" ? "text-destructive" : "text-emerald-700 dark:text-emerald-400",
          )}
        >
          {state.message}
        </p>
      )}
    </div>
  );
}

export default ActionButton;
