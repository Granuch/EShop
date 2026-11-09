// Formatting for admin pages. Dates are UTC with a "UTC" label (PLAN Q11), built from toISOString() rather than
// Intl, so the output does not depend on the runtime's locale data.

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });

/** Server totals are shown as sent, never recomputed. */
export function formatMoney(amount: number): string {
  return money.format(amount);
}

function toDate(value: string | Date): Date | null {
  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** "2026-10-03 14:05 UTC". Parses the instant, so `Z` and `+00:00` forms both work (F-35). */
export function formatDateTime(value: string | Date | null | undefined): string {
  const date = value ? toDate(value) : null;
  if (!date) return "—";
  const iso = date.toISOString();
  return `${iso.slice(0, 10)} ${iso.slice(11, 16)} UTC`;
}

/** "2026-10-03" (UTC). */
export function formatDate(value: string | Date | null | undefined): string {
  const date = value ? toDate(value) : null;
  return date ? date.toISOString().slice(0, 10) : "—";
}

const DATE_INPUT = /^\d{4}-\d{2}-\d{2}$/;

/**
 * `<input type="date">` values to inclusive UTC query bounds (conventions §10: always send `Z`; a bare date means
 * the start of the day). Anything that is not YYYY-MM-DD gives undefined, so the filter is dropped.
 */
export function startOfDayUtc(value: string | undefined): string | undefined {
  return value && DATE_INPUT.test(value) ? `${value}T00:00:00Z` : undefined;
}

export function endOfDayUtc(value: string | undefined): string | undefined {
  return value && DATE_INPUT.test(value) ? `${value}T23:59:59.999Z` : undefined;
}
