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
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { Textarea } from "@/components/ui/textarea";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";
import type { ShippingAddress } from "@/lib/admin/types/ordering";

type Action = (state: FormState, formData: FormData) => Promise<FormState>;

const invalidProps = (state: FormState, name: string, id?: string) =>
  state.fieldErrors?.[name]?.length ? { "aria-invalid": true, "aria-describedby": id ?? `${name}-error` } : {};

/** The reason is required and kept on the order; the confirm dialog submits the form outside its portal. */
export function CancelOrderForm({ action, orderLabel }: { action: Action; orderLabel: string }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const [open, setOpen] = useState(false);
  const formId = useId();
  const value = state.status === "error" ? (state.values?.reason ?? "") : "";

  return (
    <form key={formKey(state)} id={formId} action={formAction} className="space-y-3" aria-label="Cancel order">
      <div className="space-y-1.5">
        <Label htmlFor="reason">Reason</Label>
        <Textarea id="reason" name="reason" required maxLength={500} rows={2} defaultValue={value} {...invalidProps(state, "reason")} />
        <p className="text-xs text-muted-foreground">Kept on the order and shown in its history.</p>
        <FieldError name="reason" state={state} />
      </div>
      <FormMessage state={state} />
      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogTrigger render={<Button type="button" variant="destructive" disabled={pending} />}>
          {pending ? "Cancelling…" : "Cancel order"}
        </AlertDialogTrigger>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Cancel this order?</AlertDialogTitle>
            <AlertDialogDescription>
              {`Order ${orderLabel} becomes Cancelled and its pending payment is cancelled too. This cannot be undone.`}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Keep the order</AlertDialogCancel>
            <AlertDialogAction type="submit" form={formId} variant="destructive" onClick={() => setOpen(false)}>
              Cancel order
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </form>
  );
}

/** One line's quantity, inline in the items table. */
export function QuantityForm({ action, quantity, productName }: { action: Action; quantity: number; productName: string }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const value = state.status === "error" ? (state.values?.quantity ?? "") : String(quantity);

  return (
    <form key={formKey(state)} action={formAction} className="flex flex-col items-end gap-1" aria-label={`Edit quantity of ${productName}`}>
      <span className="flex items-center gap-1">
        <Input
          name="quantity"
          type="number"
          min="1"
          step="1"
          required
          aria-label={`Quantity of ${productName}`}
          defaultValue={value}
          className="h-7 w-16 text-right"
        />
        <Button type="submit" variant="outline" size="sm" disabled={pending}>
          {pending ? "…" : "Set"}
        </Button>
      </span>
      {state.status === "error" && state.message && (
        <span role="alert" className="max-w-48 text-right text-xs whitespace-normal text-destructive">
          {state.message}
        </span>
      )}
    </form>
  );
}

/** Adds an Active product: a picker when the catalog can be read, otherwise its id. */
export function AddItemForm({ action, products }: { action: Action; products: { id: string; label: string }[] | null }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const value = (name: string) => (state.status === "error" ? state.values?.[name] : undefined) ?? "";

  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label="Add product">
      <div className="grid gap-3 sm:grid-cols-[1fr_6rem]">
        <div className="space-y-1.5">
          <Label htmlFor="productId">Product</Label>
          {products ? (
            <NativeSelect id="productId" name="productId" defaultValue={value("productId")} {...invalidProps(state, "productId")}>
              <NativeSelectOption value="">Choose an active product…</NativeSelectOption>
              {products.map((product) => (
                <NativeSelectOption key={product.id} value={product.id}>
                  {product.label}
                </NativeSelectOption>
              ))}
            </NativeSelect>
          ) : (
            <Input id="productId" name="productId" placeholder="Product id" defaultValue={value("productId")} {...invalidProps(state, "productId")} />
          )}
          <FieldError name="productId" state={state} />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="add-quantity">Quantity</Label>
          <Input
            id="add-quantity"
            name="quantity"
            type="number"
            min="1"
            step="1"
            required
            defaultValue={value("quantity") || "1"}
            {...invalidProps(state, "quantity", "quantity-error")}
          />
          <FieldError name="quantity" state={state} />
        </div>
      </div>
      <p className="text-xs text-muted-foreground">Priced from the catalog now. The payment follows the new total.</p>
      <FormMessage state={state} />
      <Button type="submit" variant="outline" size="sm" disabled={pending}>
        {pending ? "Adding…" : "Add product"}
      </Button>
    </form>
  );
}

const ADDRESS: { name: keyof ShippingAddress; label: string; min: number; max: number }[] = [
  { name: "street", label: "Street", min: 3, max: 150 },
  { name: "city", label: "City", min: 2, max: 100 },
  { name: "state", label: "State / region", min: 2, max: 100 },
  { name: "zipCode", label: "Postal code", min: 3, max: 12 },
  { name: "country", label: "Country (2 letters)", min: 2, max: 2 },
];

/** All five fields: a full replacement. The character rules are the API's (its message lands on the field). */
export function AddressForm({ action, address }: { action: Action; address: ShippingAddress }) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const id = useId();

  return (
    <form key={formKey(state)} action={formAction} className="space-y-3" aria-label="Edit shipping address">
      {ADDRESS.map((field) => (
        <div key={field.name} className="space-y-1">
          <Label htmlFor={`${id}-${field.name}`}>{field.label}</Label>
          <Input
            id={`${id}-${field.name}`}
            name={field.name}
            required
            minLength={field.min}
            maxLength={field.max}
            pattern={field.name === "country" ? "[A-Za-z]{2}" : undefined}
            defaultValue={(state.status === "error" ? state.values?.[field.name] : undefined) ?? address[field.name]}
            {...invalidProps(state, field.name)}
          />
          <FieldError name={field.name} state={state} />
        </div>
      ))}
      <FormMessage state={state} />
      <Button type="submit" variant="outline" size="sm" disabled={pending}>
        {pending ? "Saving…" : "Save address"}
      </Button>
    </form>
  );
}
