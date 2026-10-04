import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select";
import type { CategoryOption } from "@/lib/admin/catalog";
import { DEFAULT_PAGE_SIZE, PRODUCTS_PATH, SORTS, STATUS_FILTERS, type ProductFilters as Filters } from "./filters";

const labelClass = "text-xs font-medium text-muted-foreground";

/** A plain GET form, like the storefront's FilterSidebar: works without JS; submitting resets the page to 1. */
function ProductFilters({ filters, categories }: { filters: Filters; categories: CategoryOption[] | null }) {
  return (
    <form action={PRODUCTS_PATH} role="search" aria-label="Filter products" className="flex flex-wrap items-end gap-3">
      <div className="flex w-full flex-col gap-1 sm:w-64">
        <label htmlFor="searchTerm" className={labelClass}>
          Search name or SKU
        </label>
        <Input
          id="searchTerm"
          name="searchTerm"
          type="search"
          defaultValue={filters.searchTerm}
          minLength={2}
          maxLength={200}
          placeholder="At least 2 characters"
        />
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="status" className={labelClass}>
          Status
        </label>
        <NativeSelect id="status" name="status" defaultValue={filters.status}>
          <NativeSelectOption value="">Any status</NativeSelectOption>
          {STATUS_FILTERS.map((status) => (
            <NativeSelectOption key={status} value={status}>
              {status}
            </NativeSelectOption>
          ))}
        </NativeSelect>
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="categoryId" className={labelClass}>
          Category
        </label>
        <NativeSelect id="categoryId" name="categoryId" defaultValue={filters.categoryId} disabled={!categories}>
          <NativeSelectOption value="">{categories ? "Any category" : "Categories unavailable"}</NativeSelectOption>
          {categories?.map((category) => (
            <NativeSelectOption key={category.id} value={category.id}>
              {`${"  ".repeat(category.depth)}${category.name}${category.isActive ? "" : " (deleted)"}`}
            </NativeSelectOption>
          ))}
        </NativeSelect>
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="hasDiscount" className={labelClass}>
          Discount
        </label>
        <NativeSelect id="hasDiscount" name="hasDiscount" defaultValue={filters.hasDiscount}>
          <NativeSelectOption value="">Any</NativeSelectOption>
          <NativeSelectOption value="true">With discount</NativeSelectOption>
          <NativeSelectOption value="false">Without discount</NativeSelectOption>
        </NativeSelect>
      </div>

      <div className="flex w-28 flex-col gap-1">
        <label htmlFor="stockBelow" className={labelClass}>
          Stock below
        </label>
        <Input id="stockBelow" name="stockBelow" type="number" min={1} step={1} defaultValue={filters.stockBelow} />
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
        <Link href={PRODUCTS_PATH} className="text-sm underline">
          Clear
        </Link>
      </div>
    </form>
  );
}

export default ProductFilters;
