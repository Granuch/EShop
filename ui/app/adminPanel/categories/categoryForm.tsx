"use client";

import { useActionState } from "react";
import { errorId, FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type CategoryFormProps = {
  mode: "create" | "edit";
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  initial: { name: string; slug: string; description: string; displayOrder: string; parentCategoryId?: string };
  /** create only: live categories by path. */
  parents?: { id: string; label: string }[];
};

/** Create or edit a category. The parent of an existing one changes through the move form. */
function CategoryForm({ mode, action, initial, parents }: CategoryFormProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const value = (name: keyof CategoryFormProps["initial"]) =>
    (state.status === "error" ? state.values?.[name] : undefined) ?? initial[name] ?? "";
  const invalid = (name: string) =>
    state.fieldErrors?.[name]?.length ? { "aria-invalid": true, "aria-describedby": errorId(name) } : {};

  return (
    <form key={formKey(state)} action={formAction} className="max-w-2xl space-y-4" aria-label={mode === "create" ? "New category" : "Edit category"}>
      <div className="space-y-1.5">
        <Label htmlFor="name">Name</Label>
        <Input id="name" name="name" required maxLength={200} defaultValue={value("name")} {...invalid("name")} />
        <FieldError name="name" state={state} />
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label htmlFor="slug">Slug</Label>
          <Input
            id="slug"
            name="slug"
            maxLength={200}
            required={mode === "edit"}
            pattern="[a-z0-9]+(-[a-z0-9]+)*"
            placeholder={mode === "create" ? "Generated from the name" : undefined}
            defaultValue={value("slug")}
            className="font-mono"
            {...invalid("slug")}
          />
          <p className="text-xs text-muted-foreground">Lower-case letters, digits and single hyphens; unique within its level.</p>
          <FieldError name="slug" state={state} />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="displayOrder">Position</Label>
          <Input
            id="displayOrder"
            name="displayOrder"
            type="number"
            min="0"
            step="1"
            placeholder="0"
            defaultValue={value("displayOrder")}
            {...invalid("displayOrder")}
          />
          <p className="text-xs text-muted-foreground">Lower comes first; ties are ordered by name.</p>
          <FieldError name="displayOrder" state={state} />
        </div>
      </div>
      {mode === "create" && (
        <div className="space-y-1.5">
          <Label htmlFor="parentCategoryId">Parent</Label>
          <NativeSelect id="parentCategoryId" name="parentCategoryId" className="w-full" defaultValue={value("parentCategoryId")} disabled={!parents} {...invalid("parentCategoryId")}>
            <NativeSelectOption value="">None (top level)</NativeSelectOption>
            {parents?.map((parent) => (
              <NativeSelectOption key={parent.id} value={parent.id}>
                {parent.label}
              </NativeSelectOption>
            ))}
          </NativeSelect>
          <FieldError name="parentCategoryId" state={state} />
        </div>
      )}
      <div className="space-y-1.5">
        <Label htmlFor="description">Description</Label>
        <Textarea id="description" name="description" maxLength={1000} rows={3} defaultValue={value("description")} {...invalid("description")} />
        {mode === "edit" && <p className="text-xs text-muted-foreground">Leave it empty to remove the description.</p>}
        <FieldError name="description" state={state} />
      </div>
      <FormMessage state={state} />
      <Button type="submit" disabled={pending}>
        {pending ? "Saving…" : mode === "create" ? "Create category" : "Save changes"}
      </Button>
    </form>
  );
}

export default CategoryForm;
