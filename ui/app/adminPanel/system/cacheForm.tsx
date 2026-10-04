"use client";

import { useActionState, useId, useState } from "react";
import { FormMessage } from "@/components/Admin/formMessage";
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
import { Label } from "@/components/ui/label";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { IDLE, type FormState } from "@/lib/admin/forms";

/** Catalog's manual cache lever, confirmed first: the next reads go to the database until the cache refills. */
function CacheForm({ action }: { action: (state: FormState, formData: FormData) => Promise<FormState> }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const [open, setOpen] = useState(false);
  const formId = useId();
  return (
    <form id={formId} action={formAction} className="space-y-3" aria-label="Invalidate the catalog cache">
      <div className="space-y-1.5">
        <Label htmlFor={`${formId}-family`}>Family</Label>
        <NativeSelect id={`${formId}-family`} name="family" defaultValue="" className="w-full">
          <NativeSelectOption value="">Both: product lists and categories</NativeSelectOption>
          <NativeSelectOption value="products:list">products:list</NativeSelectOption>
          <NativeSelectOption value="categories:list">categories:list (also category details)</NativeSelectOption>
        </NativeSelect>
        <p className="text-xs text-muted-foreground">
          For data changed behind the application&apos;s back. A product&apos;s own page is not in a family; it lapses
          within 5 minutes.
        </p>
      </div>
      <FormMessage state={state} />
      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogTrigger render={<Button type="button" variant="outline" disabled={pending} />}>
          {pending ? "Invalidating…" : "Invalidate"}
        </AlertDialogTrigger>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Invalidate the catalog cache?</AlertDialogTitle>
            <AlertDialogDescription>
              Every cached list in the chosen family stops being read, so the next requests go to the database. Nothing
              is lost.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction type="submit" form={formId} onClick={() => setOpen(false)}>
              Invalidate
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </form>
  );
}

export default CacheForm;
