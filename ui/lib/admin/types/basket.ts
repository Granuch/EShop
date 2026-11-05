// Copied from docs/01-overview/frontend/basket.md (TypeScript): the basket read and the admin part.

export interface BasketItem {
  productId: string;
  productName: string;
  /** The effective price (discountPrice ?? price) as of this line's last add/refresh. */
  price: number;
  quantity: number;
  subTotal: number;
}

export interface Basket {
  userId: string;
  items: BasketItem[];
  totalPrice: number;
  totalItems: number;
  createdAt: string | null;
  lastModifiedAt: string | null;
}

// ---- Admin ----

export interface BasketScanQuery {
  /** A nextCursor value this API returned; omit for the first page. */
  cursor?: string;
  /** Default 20, 1-100. */
  pageSize?: number;
}

export interface AbandonedBasketsQuery extends BasketScanQuery {
  /** "90m" | "24h" | "3d" form. Default "24h", max 30 days. */
  olderThan?: string;
}

export interface AdminBasketSummaryDto {
  userId: string;
  isReadable: boolean;
  lines: number | null;
  totalItems: number | null;
  totalPrice: number | null;
  currency: string | null;
  createdAt: string | null;
  lastModifiedAt: string | null;
}

export interface AdminBasketPageDto {
  items: AdminBasketSummaryDto[];
  nextCursor: string | null;
  /** Set only by /abandoned. */
  modifiedBefore: string | null;
}

export type OutboxDeadLetterError = 'PublishFailed' | 'PublishTimedOut' | 'Unpublishable';

export interface OutboxDeadLetterDto {
  isReadable: boolean;
  messageId: string | null;
  eventType: string | null;
  occurredOnUtc: string | null;
  deadLetteredAtUtc: string | null;
  attempts: number | null;
  error: OutboxDeadLetterError | null;
  exceptionType: string | null;
  correlationId: string | null;
}

export interface OutboxDeadLetterPageDto {
  total: number;
  offset: number;
  limit: number;
  items: OutboxDeadLetterDto[];
}

export interface OutboxDeadLetterCount {
  count: number;
}

export interface OutboxReplayResult {
  replayed: number;
}

/** GET /admin/outbox/dead-letters/details: offset paging over a Redis list. */
export interface OutboxDeadLettersQuery {
  /** Default 0, >= 0. */
  offset?: number;
  /** Default 20, 1-100. */
  limit?: number;
}
