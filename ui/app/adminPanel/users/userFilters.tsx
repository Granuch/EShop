import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import { DEFAULT_PAGE_SIZE, SORTS, STATES, USERS_PATH, type UserFilters as Filters } from "./filters";

const labelClass = "text-xs font-medium text-muted-foreground";

function YesNo({ name, label, value }: { name: string; label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={name} className={labelClass}>
        {label}
      </label>
      <NativeSelect id={name} name={name} defaultValue={value}>
        <NativeSelectOption value="">Any</NativeSelectOption>
        <NativeSelectOption value="true">Yes</NativeSelectOption>
        <NativeSelectOption value="false">No</NativeSelectOption>
      </NativeSelect>
    </div>
  );
}

/** A plain GET form; submitting resets the page to 1. */
function UserFilters({ filters, roles }: { filters: Filters; roles: string[] | null }) {
  return (
    <form action={USERS_PATH} role="search" aria-label="Filter users" className="flex flex-wrap items-end gap-3">
      <div className="flex w-full flex-col gap-1 sm:w-64">
        <label htmlFor="search" className={labelClass}>
          Email or name
        </label>
        <Input id="search" name="search" type="search" maxLength={256} defaultValue={filters.search} />
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor="role" className={labelClass}>
          Role
        </label>
        <NativeSelect id="role" name="role" defaultValue={filters.role}>
          <NativeSelectOption value="">Any role</NativeSelectOption>
          {(roles ?? (filters.role ? [filters.role] : [])).map((role) => (
            <NativeSelectOption key={role} value={role}>
              {role}
            </NativeSelectOption>
          ))}
        </NativeSelect>
      </div>
      <div className="flex flex-col gap-1">
        <label htmlFor="state" className={labelClass}>
          State
        </label>
        <NativeSelect id="state" name="state" defaultValue={filters.state}>
          {Object.entries(STATES).map(([key, label]) => (
            <NativeSelectOption key={key} value={key}>
              {label}
            </NativeSelectOption>
          ))}
        </NativeSelect>
      </div>
      <YesNo name="emailConfirmed" label="Email confirmed" value={filters.emailConfirmed} />
      <YesNo name="twoFactorEnabled" label="2FA" value={filters.twoFactorEnabled} />
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
      <Button type="submit">Apply</Button>
      <Link href={USERS_PATH} className="pb-1.5 text-sm underline">
        Clear
      </Link>
    </form>
  );
}

export default UserFilters;
