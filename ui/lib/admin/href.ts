export type QueryValue = string | number | boolean | readonly string[] | null | undefined;

/**
 * Builds "/adminPanel/…?a=1&b=2", skipping empty values, like lib/temp.ts's buildHref but for any path.
 * An array repeats its key (`statuses=Paid&statuses=Shipped`), which is how the API reads list filters.
 */
export function buildAdminHref(path: string, params: Record<string, QueryValue> = {}): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === "") continue;
    if (Array.isArray(value)) {
      for (const item of value) if (item) query.append(key, item);
    } else {
      query.set(key, String(value));
    }
  }
  const qs = query.toString();
  return qs ? `${path}?${qs}` : path;
}
