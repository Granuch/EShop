import { cache } from "react";
import { endOfDayUtc, startOfDayUtc } from "@/lib/admin/format";
import { getOrderStats } from "@/lib/admin/ordering";

export const DAYS = 30;

const DAY_MS = 86_400_000;

/** The last 30 whole UTC days, today included: from the start of day −29 to the end of today. */
export function statsWindow(now = new Date()): { from: string; to: string } {
  const day = (offset: number) => new Date(now.getTime() - offset * DAY_MS).toISOString().slice(0, 10);
  return { from: startOfDayUtc(day(DAYS - 1))!, to: endOfDayUtc(day(0))! };
}

/** The 30 UTC dates of the window, oldest first, as "YYYY-MM-DD". */
export function windowDays(from: string): string[] {
  const start = new Date(from).getTime();
  return Array.from({ length: DAYS }, (_, i) => new Date(start + i * DAY_MS).toISOString().slice(0, 10));
}

/** The 30-day order stats, read once per request: the stats card and the sales chart share it. */
export const loadOrderStats = cache(() => getOrderStats({ ...statsWindow(), groupBy: "Day" }));
