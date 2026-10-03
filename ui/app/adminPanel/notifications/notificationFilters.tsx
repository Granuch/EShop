import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import {
  DEFAULT_PAGE_SIZE,
  NOTIFICATION_STATUSES,
  NOTIFICATIONS_PATH,
  TEMPLATE_NAMES,
  type NotificationFilters as Filters,
} from "./filters";

const labelClass = "text-xs font-medium text-muted-foreground";

/** A plain GET form: the status boxes submit `status` once per ticked box, which is what the API reads. */
function NotificationFilters({ filters }: { filters: Filters }) {
  return (
    <form action={NOTIFICATIONS_PATH} role="search" aria-label="Filter notifications" className="space-y-3">
      <fieldset className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <legend className={`${labelClass} mb-1`}>Status (any of)</legend>
        {NOTIFICATION_STATUSES.map((status) => (
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
          <label htmlFor="templateName" className={labelClass}>
            Template
          </label>
          <NativeSelect id="templateName" name="templateName" defaultValue={filters.templateName}>
            <NativeSelectOption value="">Any template</NativeSelectOption>
            {TEMPLATE_NAMES.map((name) => (
              <NativeSelectOption key={name} value={name}>
                {name}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <div className="flex w-full flex-col gap-1 sm:w-64">
          <label htmlFor="email" className={labelClass}>
            Recipient email (exact)
          </label>
          <Input id="email" name="email" type="email" maxLength={320} defaultValue={filters.email} />
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
        <div className="flex flex-col gap-1">
          <label htmlFor="hasError" className={labelClass}>
            Has an error
          </label>
          <NativeSelect id="hasError" name="hasError" defaultValue={filters.hasError}>
            <NativeSelectOption value="">Any</NativeSelectOption>
            <NativeSelectOption value="true">Yes</NativeSelectOption>
            <NativeSelectOption value="false">No</NativeSelectOption>
          </NativeSelect>
        </div>
        {filters.pageSize !== DEFAULT_PAGE_SIZE && <input type="hidden" name="pageSize" value={filters.pageSize} />}
        <div className="flex items-center gap-3">
          <Button type="submit">Apply</Button>
          <Link href={NOTIFICATIONS_PATH} className="text-sm underline">
            Clear
          </Link>
        </div>
      </div>
    </form>
  );
}

export default NotificationFilters;
