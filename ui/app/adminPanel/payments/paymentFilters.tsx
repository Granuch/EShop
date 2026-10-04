import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import {
  DEFAULT_PAGE_SIZE,
  PAYMENT_METHODS,
  PAYMENT_STATUSES,
  PAYMENTS_PATH,
  type PaymentFilters as Filters,
} from "./filters";

const labelClass = "text-xs font-medium text-muted-foreground";

/** A plain GET form: the status boxes submit `status` once per ticked box, which is what the API reads. */
function PaymentFilters({ filters }: { filters: Filters }) {
  return (
    <form action={PAYMENTS_PATH} role="search" aria-label="Filter payments" className="space-y-3">
      <fieldset className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <legend className={`${labelClass} mb-1`}>Status (any of)</legend>
        {PAYMENT_STATUSES.map((status) => (
          <label key={status} className="flex items-center gap-1.5 text-sm">
            <input
              type="checkbox"
              name="status"
              value={status}
              defaultChecked={filters.status.includes(status)}
              className="size-4 accent-primary"
            />
            {status}
          </label>
        ))}
      </fieldset>
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1">
          <label htmlFor="paymentMethod" className={labelClass}>
            Method
          </label>
          <NativeSelect id="paymentMethod" name="paymentMethod" defaultValue={filters.paymentMethod}>
            <NativeSelectOption value="">Any method</NativeSelectOption>
            {PAYMENT_METHODS.map((method) => (
              <NativeSelectOption key={method} value={method}>
                {method === "Mock" ? "Mock (offline or simulator)" : method}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <div className="flex w-full flex-col gap-1 sm:w-80">
          <label htmlFor="orderId" className={labelClass}>
            Order id (exact)
          </label>
          <Input id="orderId" name="orderId" defaultValue={filters.orderId} className="font-mono" />
        </div>
        <div className="flex w-full flex-col gap-1 sm:w-80">
          <label htmlFor="userId" className={labelClass}>
            User id (exact)
          </label>
          <Input id="userId" name="userId" maxLength={100} defaultValue={filters.userId} className="font-mono" />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="from" className={labelClass}>
            Created from (UTC)
          </label>
          <Input id="from" name="from" type="date" defaultValue={filters.from} />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="to" className={labelClass}>
            Created to (UTC, inclusive)
          </label>
          <Input id="to" name="to" type="date" defaultValue={filters.to} />
        </div>
        <div className="flex w-28 flex-col gap-1">
          <label htmlFor="minAmount" className={labelClass}>
            Min amount
          </label>
          <Input id="minAmount" name="minAmount" type="number" min={0} step="0.01" defaultValue={filters.minAmount} />
        </div>
        <div className="flex w-28 flex-col gap-1">
          <label htmlFor="maxAmount" className={labelClass}>
            Max amount
          </label>
          <Input id="maxAmount" name="maxAmount" type="number" min={0} step="0.01" defaultValue={filters.maxAmount} />
        </div>
        {filters.pageSize !== DEFAULT_PAGE_SIZE && <input type="hidden" name="pageSize" value={filters.pageSize} />}
        <div className="flex items-center gap-3">
          <Button type="submit">Apply</Button>
          <Link href={PAYMENTS_PATH} className="text-sm underline">
            Clear
          </Link>
        </div>
      </div>
    </form>
  );
}

export default PaymentFilters;
