import Link from "next/link";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { buildAdminHref, type QueryValue } from "@/lib/admin/href";

type PagerProps = {
  path: string;
  /** The current query, without pageNumber. */
  params: Record<string, QueryValue>;
  pageNumber: number;
  totalPages: number;
  totalCount: number;
};

/** Prev / "Page X of Y" / Next from a PagedResult. A page past the end points Prev at the last real page. */
function Pager({ path, params, pageNumber, totalPages, totalCount }: PagerProps) {
  if (totalCount === 0) return null;

  const pastEnd = pageNumber > totalPages;
  const prev = pastEnd ? totalPages : pageNumber > 1 ? pageNumber - 1 : null;
  const next = !pastEnd && pageNumber < totalPages ? pageNumber + 1 : null;
  const href = (page: number) => buildAdminHref(path, { ...params, pageNumber: page === 1 ? undefined : page });

  return (
    <nav aria-label="Pagination" className="flex items-center justify-between gap-4">
      <p className="text-sm text-muted-foreground">
        Page {pageNumber} of {Math.max(totalPages, 1)}
      </p>
      <div className="flex items-center gap-2">
        {prev !== null ? (
          <Button variant="outline" size="sm" nativeButton={false} render={<Link href={href(prev)} rel="prev" />}>
            <ChevronLeft aria-hidden data-icon="inline-start" />
            Previous
          </Button>
        ) : (
          <Button variant="outline" size="sm" disabled>
            <ChevronLeft aria-hidden data-icon="inline-start" />
            Previous
          </Button>
        )}
        {next !== null ? (
          <Button variant="outline" size="sm" nativeButton={false} render={<Link href={href(next)} rel="next" />}>
            Next
            <ChevronRight aria-hidden data-icon="inline-end" />
          </Button>
        ) : (
          <Button variant="outline" size="sm" disabled>
            Next
            <ChevronRight aria-hidden data-icon="inline-end" />
          </Button>
        )}
      </div>
    </nav>
  );
}

export default Pager;
