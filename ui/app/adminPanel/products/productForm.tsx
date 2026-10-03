"use client";

import { useActionState, useState } from "react";
import { errorId, FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";
import { IDLE, type FormState } from "@/lib/admin/forms";

export type CategoryChoice = { id: string; label: string };

export type ProductFormValues = {
  name: string;
  sku: string;
  price: string;
  categoryId: string;
  description: string;
};

type ProductFormProps = {
  mode: "create" | "edit";
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  categories: CategoryChoice[];
  initial?: ProductFormValues;
};

/** Create's SKU rule, applied to edits too: the API's update does not enforce it (F-41). */
export const SKU_PATTERN = "[A-Za-z0-9_\\-]+";

function imageLines(text: string) {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function ProductForm({ mode, action, categories, initial }: ProductFormProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  // After an action React resets the fields to their defaultValue, so the submitted values come back through state.
  const value = (name: string, fallback = "") => state.values?.[name] ?? fallback;
  const [imageText, setImageText] = useState("");
  const previews = imageLines(state.values?.imageUrls ?? imageText).slice(0, 10);

  const invalid = (name: string) =>
    state.fieldErrors?.[name]?.length ? { "aria-invalid": true, "aria-describedby": errorId(name) } : {};

  return (
    <form action={formAction} className="space-y-5">
      <FormMessage state={state} />

      <div className="grid gap-5 sm:grid-cols-2">
        <div className="space-y-1.5 sm:col-span-2">
          <Label htmlFor="name">Name</Label>
          <Input id="name" name="name" required maxLength={200} defaultValue={value("name", initial?.name)} {...invalid("name")} />
          <FieldError name="name" state={state} />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="sku">SKU</Label>
          <Input
            id="sku"
            name="sku"
            required
            maxLength={50}
            pattern={SKU_PATTERN}
            title="Letters, digits, - and _ only"
            className="font-mono"
            defaultValue={value("sku", initial?.sku)}
            {...invalid("sku")}
          />
          <p className="text-xs text-muted-foreground">Letters, digits, - and _. Case-sensitive.</p>
          <FieldError name="sku" state={state} />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="categoryId">Category</Label>
          <NativeSelect
            id="categoryId"
            name="categoryId"
            required
            className="w-full"
            defaultValue={value("categoryId", initial?.categoryId)}
            {...invalid("categoryId")}
          >
            <NativeSelectOption value="">Choose a category</NativeSelectOption>
            {categories.map((category) => (
              <NativeSelectOption key={category.id} value={category.id}>
                {category.label}
              </NativeSelectOption>
            ))}
          </NativeSelect>
          <FieldError name="categoryId" state={state} />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="price">Price (USD)</Label>
          <Input
            id="price"
            name="price"
            type="number"
            required
            min="0.01"
            step="0.01"
            inputMode="decimal"
            defaultValue={value("price", initial?.price)}
            {...invalid("price")}
          />
          <FieldError name="price" state={state} />
        </div>

        {mode === "create" && (
          <div className="space-y-1.5">
            <Label htmlFor="stockQuantity">Initial stock</Label>
            <Input
              id="stockQuantity"
              name="stockQuantity"
              type="number"
              required
              min="0"
              step="1"
              defaultValue={value("stockQuantity", "0")}
              {...invalid("stockQuantity")}
            />
            <FieldError name="stockQuantity" state={state} />
          </div>
        )}

        <div className="space-y-1.5 sm:col-span-2">
          <Label htmlFor="description">Description</Label>
          <Textarea
            id="description"
            name="description"
            rows={4}
            maxLength={1000}
            defaultValue={value("description", initial?.description)}
            {...invalid("description")}
          />
          {mode === "edit" && <p className="text-xs text-muted-foreground">Leave it empty to remove the description.</p>}
          <FieldError name="description" state={state} />
        </div>

        {mode === "create" && (
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="imageUrls">Image URLs</Label>
            <Textarea
              id="imageUrls"
              name="imageUrls"
              rows={3}
              placeholder="https://… (one per line)"
              defaultValue={value("imageUrls")}
              onChange={(event) => setImageText(event.target.value)}
              {...invalid("images")}
            />
            <p className="text-xs text-muted-foreground">
              One absolute http(s) URL per line, at most 10. The first becomes the main image.
            </p>
            <FieldError name="images" state={state} />
            {previews.length > 0 && (
              <ul className="flex flex-wrap gap-2" aria-label="Image previews">
                {previews.map((url, index) => (
                  <li key={`${index}-${url}`}>
                    {/* A plain <img>: any host is allowed here; next.config only allows picsum (PLAN Q7). */}
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img src={url} alt="" className="size-16 rounded-md bg-muted object-cover ring-1 ring-foreground/10" />
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </div>

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={pending}>
          {pending ? "Saving…" : mode === "create" ? "Create draft" : "Save changes"}
        </Button>
        {mode === "create" && <p className="text-xs text-muted-foreground">New products start as drafts.</p>}
      </div>
    </form>
  );
}

export default ProductForm;
