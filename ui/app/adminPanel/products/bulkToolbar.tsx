"use client";

import { useActionState, useState, useSyncExternalStore } from "react";
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
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { IDLE } from "@/lib/admin/forms";
import { cn } from "@/lib/utils";
import { bulkProductsAction, type BulkState } from "./bulkActions";

export const BULK_FORM_ID = "bulk-products";

const selector = `input[type="checkbox"][name="ids"][form="${BULK_FORM_ID}"]`;

/**
 * The row checkboxes live in the server-rendered table, so their state is read from the DOM: on any change event, and
 * when rows come or go (a bulk delete removes them, paging replaces them).
 */
function subscribe(onChange: () => void) {
  document.addEventListener("change", onChange);
  const observer = new MutationObserver(onChange);
  observer.observe(document.body, { childList: true, subtree: true });
  return () => {
    document.removeEventListener("change", onChange);
    observer.disconnect();
  };
}

function useRowSelection() {
  const boxes = () => document.querySelectorAll<HTMLInputElement>(selector);
  const selected = useSyncExternalStore(subscribe, () => document.querySelectorAll(`${selector}:checked`).length, () => 0);
  const allOnPage = useSyncExternalStore(
    subscribe,
    () => boxes().length > 0 && [...boxes()].every((box) => box.checked),
    () => false,
  );
  const rows = useSyncExternalStore(subscribe, () => boxes().length, () => 0);
  return { selected, allOnPage, rows };
}

/** The header checkbox: ticks or clears every row on this page. */
export function SelectPageCheckbox() {
  const { allOnPage } = useRowSelection();

  return (
    <input
      type="checkbox"
      aria-label="Select every product on this page"
      className="size-4 accent-primary"
      checked={allOnPage}
      onChange={(event) => {
        for (const box of document.querySelectorAll<HTMLInputElement>(selector)) box.checked = event.target.checked;
        // Setting .checked fires no event; tell the subscribers.
        document.dispatchEvent(new Event("change"));
      }}
    />
  );
}

type BulkToolbarProps = {
  categories: { id: string; label: string }[] | null;
  /** Names for the report, by product id (the rows on this page). */
  names: Record<string, string>;
};

/**
 * The bulk form. Its inputs are the rows' checkboxes and price fields (associated through the `form` attribute), plus
 * the controls here; the clicked button's `op` names the action. Catalog's bulk bucket is 10 requests / 60 s.
 */
function BulkToolbar({ categories, names }: BulkToolbarProps) {
  const [state, formAction, pending] = useActionState<BulkState, FormData>(bulkProductsAction, IDLE);
  const { selected, rows } = useRowSelection();
  const [confirmOpen, setConfirmOpen] = useState(false);

  const none = selected === 0 || pending;
  const failures = state.report?.items.filter((item) => !item.succeeded) ?? [];
  // Stays mounted on an empty page so the report of a delete that emptied it is still shown.
  if (rows === 0 && !state.message) return null;

  return (
    <div className="space-y-3 rounded-lg border p-3">
      <form id={BULK_FORM_ID} action={formAction} aria-label="Bulk actions" className="flex flex-wrap items-end gap-2">
        <p className="mr-2 self-center text-sm font-medium" aria-live="polite">
          {pending ? "Working…" : `${selected} selected`}
        </p>
        <Button type="submit" name="op" value="publish" variant="outline" size="sm" disabled={none}>
          Publish
        </Button>
        <Button type="submit" name="op" value="unpublish" variant="outline" size="sm" disabled={none}>
          Unpublish
        </Button>

        <span className="flex items-end gap-1">
          <NativeSelect name="categoryId" aria-label="Category to move to" size="sm" defaultValue="" disabled={!categories}>
            <NativeSelectOption value="">{categories ? "Move to category…" : "Categories unavailable"}</NativeSelectOption>
            {categories?.map((category) => (
              <NativeSelectOption key={category.id} value={category.id}>
                {category.label}
              </NativeSelectOption>
            ))}
          </NativeSelect>
          <Button type="submit" name="op" value="category" variant="outline" size="sm" disabled={none || !categories}>
            Move
          </Button>
        </span>

        <span className="flex items-end gap-1">
          <NativeSelect name="priceMode" aria-label="Price change" size="sm" defaultValue="set">
            <NativeSelectOption value="set">Set price to</NativeSelectOption>
            <NativeSelectOption value="percent">Change price by %</NativeSelectOption>
          </NativeSelect>
          <Input
            name="amount"
            type="number"
            step="0.01"
            aria-label="Price or percentage"
            className="h-7 w-24"
            // Enter would submit with the form's first button, Publish: keep it from doing anything here.
            onKeyDown={(event) => event.key === "Enter" && event.preventDefault()}
          />
          <Button type="submit" name="op" value="price" variant="outline" size="sm" disabled={none}>
            Apply
          </Button>
        </span>

        <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
          <AlertDialogTrigger render={<Button variant="destructive" size="sm" disabled={none} />}>Delete</AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{`Delete ${selected} ${selected === 1 ? "product" : "products"}?`}</AlertDialogTitle>
              <AlertDialogDescription>
                They leave the shop and every admin list, and their SKUs become free. Each can be restored from the
                recycle bin, as a draft.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Cancel</AlertDialogCancel>
              <AlertDialogAction
                type="submit"
                form={BULK_FORM_ID}
                name="op"
                value="delete"
                variant="destructive"
                onClick={() => setConfirmOpen(false)}
              >
                Delete products
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </form>

      {state.message && (
        <div
          role={state.status === "error" ? "alert" : "status"}
          className={cn("text-sm", state.status === "error" ? "text-destructive" : "text-emerald-700 dark:text-emerald-400")}
        >
          <p>{state.message}</p>
          {failures.length > 0 && (
            <ul className="mt-1 list-disc pl-5">
              {failures.map((item) => (
                <li key={item.productId}>
                  <span className="font-medium">{names[item.productId] ?? item.productId}</span>: {item.error ?? item.errorCode}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

export default BulkToolbar;
