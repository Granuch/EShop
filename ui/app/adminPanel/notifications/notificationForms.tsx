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

/** Retry-failed for the journal's current filters (sent as hidden fields), with an optional limit. */
export function RetryFailedForm({ action, filters }: { action: Action; filters: Record<string, string> }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const id = useId();
  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label="Retry failed">
      {Object.entries(filters).map(([name, value]) => (
        <input key={name} type="hidden" name={name} value={value} />
      ))}
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex w-28 flex-col gap-1">
          <Label htmlFor={`${id}-limit`} className="text-xs text-muted-foreground">
            Limit (1–100)
          </Label>
          <Input id={`${id}-limit`} name="limit" type="number" min={1} max={100} placeholder="100" />
        </div>
        <Button type="submit" variant="outline" disabled={pending}>
          {pending ? "Retrying…" : "Retry failed"}
        </Button>
      </div>
      <FormMessage state={state} />
    </form>
  );
}

/** Close the row for good, with a reason; confirmed first (nothing is ever sent for it again). */
export function MarkUndeliverableForm({ action }: { action: Action }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const [open, setOpen] = useState(false);
  const formId = useId();
  return (
    <form key={formKey(state)} id={formId} action={formAction} className="space-y-3" aria-label="Mark undeliverable">
      <div className="space-y-1.5">
        <Label htmlFor={`${formId}-reason`}>Reason</Label>
        <Input
          id={`${formId}-reason`}
          name="reason"
          required
          maxLength={500}
          placeholder="e.g. the address bounces permanently"
          defaultValue={state.status === "error" ? (state.values?.reason ?? "") : ""}
          aria-invalid={state.fieldErrors?.reason?.length ? true : undefined}
          aria-describedby={state.fieldErrors?.reason?.length ? "reason-error" : undefined}
        />
        <FieldError name="reason" state={state} />
      </div>
      <FormMessage state={state} />
      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogTrigger render={<Button type="button" variant="destructive" disabled={pending} />}>
          {pending ? "Saving…" : "Mark undeliverable"}
        </AlertDialogTrigger>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Mark this notification undeliverable?</AlertDialogTitle>
            <AlertDialogDescription>Nothing is ever sent for it again, by anyone. This cannot be undone.</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction type="submit" form={formId} variant="destructive" onClick={() => setOpen(false)}>
              Mark undeliverable
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </form>
  );
}

/** A test send of one template to any address: the real email with sample data, answered synchronously. */
export function TestSendForm({ action, templateName }: { action: Action; templateName: string }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const id = useId();
  const value = (name: string) => (state.status === "error" ? (state.values?.[name] ?? "") : "");
  return (
    <form key={formKey(state)} action={formAction} className="space-y-2" aria-label={`Test ${templateName}`}>
      <div className="grid gap-2 sm:grid-cols-[1fr_10rem_auto] sm:items-end">
        <div className="space-y-1">
          <Label htmlFor={`${id}-email`} className="text-xs">
            Send to
          </Label>
          <Input
            id={`${id}-email`}
            name="email"
            type="email"
            required
            maxLength={320}
            defaultValue={value("email")}
            aria-invalid={state.fieldErrors?.email?.length ? true : undefined}
          />
        </div>
        <div className="space-y-1">
          <Label htmlFor={`${id}-name`} className="text-xs">
            Greet as (optional)
          </Label>
          <Input id={`${id}-name`} name="name" maxLength={100} defaultValue={value("name")} />
        </div>
        <Button type="submit" variant="outline" disabled={pending}>
          {pending ? "Sending…" : "Send test"}
        </Button>
      </div>
      <FieldError name="email" state={state} />
      <FormMessage state={state} />
    </form>
  );
}
