// Copied from docs/01-overview/frontend/admin-platform.md (TypeScript). Only what the admin uses so far;
// the audit, settings and feature-flag types arrive with P13.

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
