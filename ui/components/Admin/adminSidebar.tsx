"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { FolderTree, LayoutDashboard, LogOut, Package, ShieldCheck, ShoppingBag, Store, Users, type LucideIcon } from "lucide-react";
import LogoutButton from "@/components/Navbar/logoutButton";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
  useSidebar,
} from "@/components/ui/sidebar";
import type { AdminIcon } from "@/lib/admin/permissions";

const ICONS: Record<AdminIcon, LucideIcon> = {
  dashboard: LayoutDashboard,
  products: Package,
  categories: FolderTree,
  orders: ShoppingBag,
  users: Users,
  roles: ShieldCheck,
};

const DASHBOARD = "/adminPanel";

export type AdminNavItem = { href: string; label: string; icon: AdminIcon };

type AdminSidebarProps = {
  items: AdminNavItem[];
  user: { name: string; email: string };
};

function isActive(pathname: string, href: string) {
  return href === DASHBOARD ? pathname === href : pathname === href || pathname.startsWith(`${href}/`);
}

function AdminSidebar({ items, user }: AdminSidebarProps) {
  const pathname = usePathname();
  const { isMobile, setOpenMobile } = useSidebar();
  // On mobile the sidebar is a sheet; close it once a link is chosen.
  const closeOnMobile = () => {
    if (isMobile) setOpenMobile(false);
  };

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton
              size="lg"
              tooltip="EShop Admin"
              render={<Link href={DASHBOARD} onClick={closeOnMobile} />}
            >
              <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-primary text-sm font-semibold text-primary-foreground">
                E
              </span>
              <span className="text-base font-semibold tracking-tight">EShop Admin</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>

      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>Manage</SidebarGroupLabel>
          <SidebarGroupContent>
            <nav aria-label="Admin">
              <SidebarMenu>
                {items.map((item) => {
                  const Icon = ICONS[item.icon];
                  const active = isActive(pathname, item.href);
                  return (
                    <SidebarMenuItem key={item.href}>
                      <SidebarMenuButton
                        isActive={active}
                        tooltip={item.label}
                        render={
                          <Link
                            href={item.href}
                            aria-current={active ? "page" : undefined}
                            onClick={closeOnMobile}
                          />
                        }
                      >
                        <Icon aria-hidden />
                        <span>{item.label}</span>
                      </SidebarMenuButton>
                    </SidebarMenuItem>
                  );
                })}
              </SidebarMenu>
            </nav>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>

      <SidebarFooter>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton tooltip="Back to the shop" render={<Link href="/" />}>
              <Store aria-hidden />
              <span>Back to the shop</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
        <div className="px-2 pt-1 text-sm group-data-[collapsible=icon]:hidden">
          <p className="truncate font-medium">{user.name}</p>
          <p className="truncate text-xs text-muted-foreground">{user.email}</p>
          <div className="mt-2 flex items-center gap-2">
            <LogOut aria-hidden className="size-4 text-red-500" />
            <LogoutButton />
          </div>
        </div>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}

export default AdminSidebar;
