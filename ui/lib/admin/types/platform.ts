// Copied from docs/01-overview/frontend/admin-platform.md (TypeScript). Keep in step with that file.

// ---- Enums (sent as strings) ----

export type AuditOutcome = 'Succeeded' | 'Rejected' | 'Failed';
/** The services whose audit trail the gateway merges. Basket keeps none. */
export type AuditService = 'catalog' | 'identity' | 'notification' | 'ordering' | 'payment';
export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy';
/** The health page's components, in the order they are listed. */
export type SystemComponent =
  | 'gateway'
  | 'identity'
  | 'catalog'
  | 'basket'
  | 'ordering'
  | 'payment'
  | 'notification';
export type PaymentProvider = 'Stripe' | 'Simulator';
export type PaymentSimulationMode = 'Random' | 'AlwaysSuccess' | 'AlwaysFailure';

// ---- Audit trail ----

export interface AuditLogQuery {
  /** The previous page's nextCursor. Keep every other parameter unchanged while paging. */
  cursor?: string;
  /** One service only. */
  service?: AuditService;
  /** Default 50, 1-100. */
  pageSize?: number;
  /** Exact, case-sensitive; <= 200 chars. */
  actorUserId?: string;
  /** Exact, case-sensitive command name without "Command", e.g. RefundPayment; <= 100 chars. */
  action?: string;
  /** Exact, case-sensitive, e.g. Product; <= 100 chars. */
  entityType?: string;
  /** Exact, case-sensitive; <= 200 chars. */
  entityId?: string;
  /** Any case. */
  outcome?: AuditOutcome;
  /** ISO instant; inclusive. */
  from?: string;
  /** ISO instant; EXCLUSIVE, and must be later than from. */
  to?: string;
}

export interface AuditLogEntry {
  /** Unique only within its service: key rows by service + id. */
  id: number;
  occurredAt: string;
  service: AuditService;
  action: string;
  entityType: string;
  entityId: string | null;
  actorUserId: string | null;
  /** The actor's email at the time. */
  actorName: string | null;
  correlationId: string | null;
  outcome: AuditOutcome;
  /** The refusal's errorCode (Rejected) or the exception type name (Failed). */
  errorCode: string | null;
  /** The input as a JSON string: masked ("****"), lists cut at 25. Parse it again; do not rely on its shape. */
  payloadJson: string | null;
}

export interface AuditLogPage {
  /** Newest first, across services. */
  items: AuditLogEntry[];
  /** null once every service has been read to its end. */
  nextCursor: string | null;
  /** Services not read for this page; their rows come on a later page. */
  unavailableServices: AuditService[];
}

// ---- System health ----

export interface HealthCheckEntry {
  /** Names differ per service; do not hard-code them. */
  name: string;
  status: HealthStatus;
}

export interface ComponentHealth {
  name: SystemComponent;
  /** false: no answer within 5 s (status is then Unhealthy, checks empty). */
  reachable: boolean;
  status: HealthStatus;
  checks: HealthCheckEntry[];
}

export interface SystemHealth {
  /** The worst component status. */
  status: HealthStatus;
  checkedAt: string;
  components: ComponentHealth[];
}

// ---- Settings ----

export interface PricingSettings {
  /** Always "USD". */
  currency: string;
  /** false: no tax is applied. */
  taxApplied: boolean;
  /** false: no shipping cost is charged. */
  shippingCharged: boolean;
}

export interface PaymentSettings {
  provider: PaymentProvider;
}

export interface SystemSettings {
  /** null when Ordering could not be read. */
  pricing: PricingSettings | null;
  /** null when Payment could not be read. */
  payments: PaymentSettings | null;
  unavailableServices: ('ordering' | 'payment')[];
}

// ---- Feature flags ----

export interface SimulationRouteFlag {
  routeId: string;
  pathPrefix: string;
  /** The route's own switch. */
  enabled: boolean;
  /** Injecting faults now: enabled AND the master switch. */
  active: boolean;
  /** 0-1. */
  errorRate: number;
  delayMinMs: number;
  delayMaxMs: number;
  forcedFailureMode: string | null;
}

export interface GatewaySimulationFlags {
  /** The master switch. */
  enabled: boolean;
  allowHeaderOverride: boolean;
  routes: SimulationRouteFlag[];
}

export interface PaymentFeatureFlags {
  /** true exactly when Stripe is off. */
  simulatorActive: boolean;
  simulationMode: PaymentSimulationMode;
  successRatePercent: number;
  processingDelayMinSeconds: number;
  processingDelayMaxSeconds: number;
  refundDelaySeconds: number;
  webhookSignatureVerificationSkipped: boolean;
}

export interface FeatureFlags {
  gatewaySimulation: GatewaySimulationFlags;
  /** null when Payment could not be read. */
  payments: PaymentFeatureFlags | null;
  unavailableServices: 'payment'[];
}

// ---- Catalog cache (catalog.md "Cache") ----

export type CacheFamily = 'products:list' | 'categories:list';

export interface CacheInvalidationReport {
  /** Always "catalog". */
  service: string;
  /** The families actually bumped, in order. */
  families: string[];
}
