import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { DEFAULT_PAGE_SIZE, ORDER_STATUSES, ORDERS_PATH, SORTS, type OrderFilters as Filters } from "./filters";

const labelClass = "text-xs font-medium text-muted-foreground";

/** A plain GET form: the status checkboxes submit `statuses` once per ticked box, which is what the API reads. */
function OrderFilters({ filters }: { filters: Filters }) {
  return (
    <form action={ORDERS_PATH} role="search" aria-label="Filter orders" className="space-y-3">
      <fieldset className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <legend className={`${labelClass} mb-1`}>Status (any of)</legend>
        {ORDER_STATUSES.map((status) => (
          <label key={status} className="flex items-center gap-1.5 text-sm">
            <input
              type="checkbox"
              name="statuses"
              value={status}
              defaultChecked={filters.statuses.includes(status)}
              className="size-4 accent-primary"
            />
            {status}
          </label>
        ))}
      </fieldset>

      <div className="flex flex-wrap items-end gap-3">
        <div className="flex w-full flex-col gap-1 sm:w-72">
          <label htmlFor="search" className={labelClass}>
            Order id, user id or payment reference
          </label>
          <Input id="search" name="search" type="search" maxLength={200} defaultValue={filters.search} />
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
          <label htmlFor="minTotal" className={labelClass}>
            Min total
          </label>
          <Input id="minTotal" name="minTotal" type="number" min={0} step="0.01" defaultValue={filters.minTotal} />
        </div>
        <div className="flex w-28 flex-col gap-1">
          <label htmlFor="maxTotal" className={labelClass}>
            Max total
          </label>
          <Input id="maxTotal" name="maxTotal" type="number" min={0} step="0.01" defaultValue={filters.maxTotal} />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="sort" className={labelClass}>
            Sort
          </label>
          <NativeSelect id="sort" name="sort" defaultValue={filters.sort}>
            {Object.entries(SORTS).map(([key, sort]) => (
              <NativeSelectOption key={key} value={key}>
                {sort.label}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        {filters.pageSize !== DEFAULT_PAGE_SIZE && <input type="hidden" name="pageSize" value={filters.pageSize} />}
        <div className="flex items-center gap-3">
          <Button type="submit">Apply</Button>
          <Link href={ORDERS_PATH} className="text-sm underline">
            Clear
          </Link>
        </div>
      </div>
    </form>
  );
}

export default OrderFilters;
