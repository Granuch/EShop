// Copied from docs/01-overview/frontend/notification.md (TypeScript). Keep in step with that file.

// ---- Enums (sent as strings) ----

export type NotificationStatus = 'Pending' | 'Sending' | 'Sent' | 'Failed' | 'Undeliverable';

export type NotificationTemplateName =
  | 'order-created'
  | 'order-shipped'
  | 'payment-created'
  | 'payment-completed'
  | 'payment-failed'
  | 'payment-refunded'
  | 'password-reset'
  | 'email-confirmation';

// ---- Requests ----

export interface NotificationFilterQuery {
  /** Repeat the parameter for a union: ?status=Failed&status=Undeliverable. */
  status?: NotificationStatus[];
  /** Exact match, any case, <= 200 chars (e.g. OrderCreatedEvent). */
  eventType?: string;
  /** Exact match, any case, <= 100 chars (e.g. order-created). */
  templateName?: string;
  /** Exact, case-sensitive match on the recipient's user id; <= 100 chars. */
  userId?: string;
  /** Exact match on the recipient address, any case; <= 320 chars. */
  email?: string;
  /** ISO instant; inclusive, on createdAt. */
  from?: string;
  /** ISO instant; inclusive, on createdAt. */
  to?: string;
  /** true: only rows with a lastError; false: only rows without one. */
  hasError?: boolean;
}

export interface NotificationListQuery extends NotificationFilterQuery {
  /** Default 1. */
  pageNumber?: number;
  /** Default 20, 1-100. */
  pageSize?: number;
}

/** The statistics take the journal's filters without paging. */
export type NotificationStatsQuery = NotificationFilterQuery;

export interface TestNotificationRequest {
  /** A valid address, <= 320 chars. */
  email: string;
  /** Whom the email greets, <= 100 chars. Omitted: "there". */
  name?: string | null;
}

export interface RetryFailedNotificationsRequest {
  eventType?: string | null;
  templateName?: string | null;
  userId?: string | null;
  email?: string | null;
  from?: string | null;
  to?: string | null;
  /** 1-100; omitted: 100. */
  limit?: number | null;
}

export interface MarkUndeliverableRequest {
  /** Not blank, <= 500 chars. */
  reason: string;
}

// ---- Responses ----

export interface NotificationSummary {
  id: string;
  /** One notification per integration event. */
  eventId: string;
  eventType: string;
  templateName: NotificationTemplateName;
  subject: string;
  status: NotificationStatus;
  /** null until the recipient is known. */
  recipientEmail: string | null;
  userId: string | null;
  /** Failed attempts. */
  retryCount: number;
  /** lastError is set; read it from the detail. */
  hasError: boolean;
  createdAt: string;
  sentAt: string | null;
  updatedAt: string | null;
}

export interface NotificationDetail extends Omit<NotificationSummary, 'hasError'> {
  correlationId: string | null;
  /** Our own wording: why the last attempt failed, or why it is Undeliverable. */
  lastError: string | null;
  /** The sent email's Message-ID. */
  providerMessageId: string | null;
  attemptStartedAt: string | null;
  /** Sent or Undeliverable: never attempted again. */
  isFinal: boolean;
  /** Not final and the event was kept. A resend can still be 409 while an attempt runs. */
  isResendable: boolean;
}

export interface NotificationStatusCount {
  status: NotificationStatus;
  count: number;
}

export interface NotificationStats {
  from: string | null;
  to: string | null;
  total: number;
  sent: number;
  failed: number;
  /** Pending + Sending. */
  queued: number;
  undeliverable: number;
  /** Always five entries: Pending, Sent, Failed, Sending, Undeliverable. */
  byStatus: NotificationStatusCount[];
}

export interface NotificationTemplate {
  name: NotificationTemplateName;
  eventType: string;
  /** false only for password-reset and email-confirmation. */
  resendable: boolean;
}

export interface TestNotificationResult {
  templateName: NotificationTemplateName;
  providerMessageId: string;
}

export interface RetryFailedNotificationsResult {
  matching: number;
  limit: number;
  /** Handed to the delivery queue, not yet delivered. */
  dispatchedIds: string[];
  /** Refused by the message bus. */
  failedIds: string[];
}
