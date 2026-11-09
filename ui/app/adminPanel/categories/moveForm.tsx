"use client";

import { useActionState } from "react";
import { FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type MoveFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  /** Live categories, without this one and its descendants (the API refuses both). */
  parents: { id: string; label: string }[];
  currentParentId: string | null;
};

/** Moves the category with its whole subtree. "" is the top level, sent as an explicit null. */
function MoveForm({ action, parents, currentParentId }: MoveFormProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const value = (state.status === "error" ? state.values?.newParentCategoryId : undefined) ?? currentParentId ?? "";

  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label="Move category">
      <div className="space-y-1.5">
        <Label htmlFor="newParentCategoryId">Parent</Label>
        <NativeSelect id="newParentCategoryId" name="newParentCategoryId" className="w-full" defaultValue={value}>
          <NativeSelectOption value="">None (top level)</NativeSelectOption>
          {parents.map((parent) => (
            <NativeSelectOption key={parent.id} value={parent.id}>
              {parent.label}
            </NativeSelectOption>
          ))}
        </NativeSelect>
        <p className="text-xs text-muted-foreground">Its subcategories and products move with it.</p>
      </div>
      <FormMessage state={state} />
      <Button type="submit" variant="outline" disabled={pending}>
        {pending ? "Moving…" : "Move"}
      </Button>
    </form>
  );
}

export default MoveForm;
