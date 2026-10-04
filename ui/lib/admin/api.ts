import "server-only";

import { apiFetch } from "@/lib/api";
import type { ProblemDetails } from "@/lib/admin/types/common";

/** An API failure. `problem` is null for the responses that have no body (conventions §3.2: 401, 403, bare 502/504). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails | null,
    /** Seconds from `Retry-After` (every 429, and the gateway's 503). */
    readonly retryAfter: number | null = null,
  ) {
    super(problem?.detail ?? problem?.title ?? `HTTP ${status}`);
    this.name = "ApiError";
  }

  get errorCode(): string | undefined {
    return this.problem?.errorCode;
  }
}

function parseRetryAfter(value: string | null): number | null {
  if (!value) return null;
  const seconds = Number(value);
  return Number.isFinite(seconds) && seconds >= 0 ? Math.ceil(seconds) : null;
}

/** Reads an error response. Several responses have no body at all (§3.2), so a null problem is a normal result. */
export async function toApiError(response: Response): Promise<ApiError> {
  const text = await response.text();
  let problem: ProblemDetails | null = null;
  if (text && (response.headers.get("content-type") ?? "").includes("json")) {
    try {
      problem = JSON.parse(text) as ProblemDetails;
    } catch {
      problem = null;
    }
  }
  return new ApiError(response.status, problem, parseRetryAfter(response.headers.get("retry-after")));
}

/** The failures a page handles itself (PLAN §2.3). Anything else is rethrown to error.tsx. */
export type ExpectedFailure =
  | { kind: "forbidden" }
  | { kind: "notFound" }
  | { kind: "rateLimited"; message: string }
  | { kind: "invalid"; message: string };

export function classifyFailure(error: unknown): ExpectedFailure | null {
  if (!(error instanceof ApiError)) return null;
  switch (error.status) {
    case 403:
      return { kind: "forbidden" };
    case 404:
      return { kind: "notFound" };
    case 429:
      return { kind: "rateLimited", message: `Too many requests — try again in ${error.retryAfter ?? 60} s.` };
    case 400:
      return { kind: "invalid", message: error.problem?.detail ?? "The request was not valid." };
    default:
      return null;
  }
}

/**
 * Server-side call to the gateway with the caller's token. Resolves with the parsed body (undefined for 204),
 * throws ApiError for any non-2xx, and lets network failures propagate.
 */
export async function adminFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await apiFetch(path, {
    ...init,
    cache: "no-store",
    headers: { Accept: "application/json", ...init.headers },
  });

  if (!response.ok) throw await toApiError(response);

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
