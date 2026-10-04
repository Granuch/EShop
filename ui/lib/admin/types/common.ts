// Copied from docs/01-overview/frontend/conventions.md (§3.5, §5, §6.1). Keep in step with that file.

/**
 * RFC 7807 body as the services send it. `type`/`title` are missing on Identity's controller failures, and on a 429
 * `type` is missing everywhere. Endpoints that add members document an interface that extends this one.
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status: number;
  detail?: string;
  errorCode: string;
  traceId: string;
  /** `ValidationError` only. Keys are camelCase field names; "$" is a rule about the whole request. */
  errors?: Record<string, string[]>;
}

export type Permission =
  | 'catalog.read' | 'catalog.write'
  | 'orders.read' | 'orders.write'
  | 'payments.read' | 'payments.write' | 'payments.refund'
  | 'users.read' | 'users.manage' | 'roles.manage'
  | 'notifications.read' | 'notifications.manage'
  | 'baskets.read' | 'audit.read' | 'system.manage';

export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
