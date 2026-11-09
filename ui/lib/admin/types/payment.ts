// Copied from docs/01-overview/frontend/payment.md (TypeScript). Keep in step with that file.

export type PaymentStatus =
  | 'Pending'
  | 'Processing'
  | 'Success'
  | 'Failed'
  | 'Refunded'
  | 'Cancelled';
/** The form the admin filters accept (any case works; send this one). */
export type PaymentStatusName = PaymentStatus;

export type PaymentMethod = 'None' | 'Mock' | 'Stripe';
export type PaymentEventKind = 'Transition' | 'Webhook';
export type PaymentStatsGroupBy = 'Day' | 'Month' | 'Year';
export type ReplayOutcome = 'Replayed' | 'AlreadyProcessed' | 'Failed' | 'NotFound';
export type SimulationMode = 'Random' | 'AlwaysSuccess' | 'AlwaysFailure';
/** Stripe's own intent status (lower case). A new intent is 'requires_payment_method'. */
export type StripeIntentStatus =
  | 'requires_payment_method'
  | 'requires_confirmation'
  | 'requires_action'
  | 'processing'
  | 'requires_capture'
  | 'canceled'
  | 'succeeded';

// ---- Storefront: requests ----

export interface CreatePaymentIntentRequest {
  orderId: string;
  /** For the Stripe customer; a valid address, <= 254 chars. Blank means none. */
  email?: string | null;
}

export interface UserPaymentsQuery {
  /** Default 1. */
  pageNumber?: number;
  /** Default 10, 1-100. */
  pageSize?: number;
}

// ---- Storefront: responses ----

export interface CreatePaymentIntentResponse {
  paymentId: string;
  paymentIntentId: string;
  /** For Stripe.js. A repeat call while the payment is Processing returns the same secret. */
  clientSecret: string;
  /** Stripe's status, not a PaymentStatus. */
  status: StripeIntentStatus;
}

export interface Payment {
  id: string;
  orderId: string;
  userId: string;
  /** The order's current total; can change while Pending or Processing. */
  amount: number;
  /** Always "USD". */
  currency: string;
  paymentMethod: PaymentMethod;
  status: PaymentStatus;
  /** Stripe pi_..., "offline:<reference>", or the simulator's fake pi_<32 hex>; null before any attempt. */
  paymentIntentId: string | null;
  /** A decline reason, a cancellation note, a simulator failure, or an admin's refund reason. */
  errorMessage: string | null;
  createdAt: string;
  processedAt: string | null;
  updatedAt: string | null;
}

// ---- Admin: requests ----

export interface PaymentFilterQuery {
  /** Repeat the parameter for a union: ?status=Refunded&status=Cancelled. */
  status?: PaymentStatusName[];
  /** Exact, case-sensitive match on the owner; <= 100 chars. */
  userId?: string;
  orderId?: string;
  paymentMethod?: PaymentMethod;
  /** ISO instant; inclusive. */
  from?: string;
  /** ISO instant; inclusive. */
  to?: string;
  /** >= 0. */
  minAmount?: number;
  /** >= minAmount. */
  maxAmount?: number;
}

export interface AdminPaymentListQuery extends PaymentFilterQuery {
  /** Default 1. */
  pageNumber?: number;
  /** Default 10, 1-100. */
  pageSize?: number;
}

/** The export takes the list's filters without paging. */
export type PaymentExportQuery = PaymentFilterQuery;

export interface PaymentStatsQuery {
  from?: string;
  to?: string;
  /** Default Day. */
  groupBy?: PaymentStatsGroupBy;
  /** Three letters, default USD. */
  currency?: string;
}

export interface SettleOfflinePaymentRequest {
  orderId: string;
  /** Not blank, <= 100 chars, unique. */
  reference: string;
}

export interface SettlePaymentRequest {
  orderId: string;
}

export interface RefundPaymentRequest {
  /** Must equal the payment's amount; omit for a full refund. */
  amount?: number | null;
  /** <= 500 chars; stored as the payment's errorMessage. */
  reason?: string | null;
}

export interface ReplayFailedStripeWebhooksRequest {
  /** <= 100 capture ids; omit for every outstanding capture. */
  ids?: string[] | null;
}

// ---- Admin: responses ----

export interface PaymentStatusBreakdown {
  status: PaymentStatus;
  count: number;
  amount: number;
}

export interface PaymentStatsBucket {
  /** Midnight UTC of the day, the 1st of the month, or 1 January. */
  periodStart: string;
  paymentCount: number;
  grossAmount: number;
  capturedRevenue: number;
  refundedAmount: number;
}

export interface PaymentStats {
  from: string | null;
  to: string | null;
  currency: string;
  groupBy: PaymentStatsGroupBy;
  totalPayments: number;
  /** Every status. Not revenue. */
  grossAmount: number;
  /** Success only. */
  capturedRevenue: number;
  refundedAmount: number;
  failedAmount: number;
  /** Always six entries, one per status. */
  byStatus: PaymentStatusBreakdown[];
  /** Non-empty periods only, oldest first. */
  buckets: PaymentStatsBucket[];
}

export interface PaymentEvent {
  id: string;
  kind: PaymentEventKind;
  /** Stripe's evt_..., on Webhook rows only. */
  stripeEventId: string | null;
  /** null on the first row only. */
  fromStatus: PaymentStatus | null;
  toStatus: PaymentStatus;
  /** Human-readable; do not parse. */
  detail: string;
  /** A user id, "system" for a message, null for a Stripe delivery. */
  actorId: string | null;
  occurredAt: string;
}

export interface FailedStripeWebhookReplayResult {
  id: string;
  stripeEventId: string | null;
  eventType: string | null;
  outcome: ReplayOutcome;
  /** Only on Failed: the raw exception type and message. */
  error: string | null;
}

export interface FailedStripeWebhookReplayReport {
  attempted: number;
  /** Replayed + AlreadyProcessed. */
  replayed: number;
  stillFailing: number;
  /** Captures still waiting after this request. */
  outstanding: number;
  results: FailedStripeWebhookReplayResult[];
}

export interface PaymentSimulationDiagnostics {
  mode: SimulationMode;
  processingDelayMinSeconds: number;
  processingDelayMaxSeconds: number;
  successRatePercent: number;
  refundDelaySeconds: number;
  randomSeed: number | null;
  forcedFailureReason: string;
}
