import type { Permission } from "@/lib/admin/types/common";

/** The 15 permissions, in the order Identity lists them (conventions §5). */
export const PERMISSIONS: readonly Permission[] = [
  "catalog.read", "catalog.write",
  "orders.read", "orders.write",
  "payments.read", "payments.write", "payments.refund",
  "users.read", "users.manage", "roles.manage",
  "notifications.read", "notifications.manage",
  "baskets.read", "audit.read", "system.manage",
];

export function isPermission(value: string): value is Permission {
  return (PERMISSIONS as readonly string[]).includes(value);
}

/** Icon keys, mapped to lucide components on the client (component functions cannot cross to the client). */
export type AdminIcon = "dashboard" | "products" | "orders";

export interface AdminSection {
  label: string;
  href: string;
  icon: AdminIcon;
  /** The permission that shows the section; null means any admin permission. */
  permission: Permission | null;
}

/** Every admin section, in sidebar order. A section is added here in the stage that builds its page. */
export const ADMIN_SECTIONS: readonly AdminSection[] = [
  { label: "Dashboard", href: "/adminPanel", icon: "dashboard", permission: null },
  { label: "Products", href: "/adminPanel/products", icon: "products", permission: "catalog.read" },
];

export function canSee(permissions: readonly Permission[], section: AdminSection): boolean {
  return section.permission === null ? permissions.length > 0 : permissions.includes(section.permission);
}

export function visibleSections(permissions: readonly Permission[]): AdminSection[] {
  return ADMIN_SECTIONS.filter((section) => canSee(permissions, section));
}

/** Services whose admin endpoints still require the `Admin` role, not a permission (conventions §5). */
export type RoleGatedService = "catalog" | "ordering" | "identity" | "payment" | "basket";

/**
 * Shown when such a service answers 403 to a caller who holds the section's permission. When a service moves to
 * permissions, delete its entry here.
 */
export const ADMIN_ROLE_HINTS: Record<RoleGatedService, string> = {
  catalog: "The catalog service still requires the Admin role for this.",
  ordering: "The ordering service still requires the Admin role for this.",
  identity: "The identity service still requires the Admin role for this.",
  payment: "The payment service still requires the Admin role for this.",
  basket: "The basket service still requires the Admin role for this.",
};
