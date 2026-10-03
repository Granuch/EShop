import Link from "next/link";
import { cookies } from "next/headers";
import AccessDenied from "@/components/Admin/accessDenied";
import AdminSidebar from "@/components/Admin/adminSidebar";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { TooltipProvider } from "@/components/ui/tooltip";
import { hasAnyPermission, loadAdminSession } from "@/lib/admin/auth";
import { visibleSections } from "@/lib/admin/permissions";

export const metadata = {
  title: "Admin · EShop",
};

/**
 * The admin shell. Not a security boundary (layouts do not re-render on navigation, and pages render in parallel):
 * every page and action checks permissions itself. It never throws, because adminPanel/error.tsx does not wrap it.
 */
export default async function AdminLayout({ children }: LayoutProps<"/adminPanel">) {
  const result = await loadAdminSession();

  if (result.status === "unavailable") {
    return (
      <div role="alert" className="flex min-h-svh items-center justify-center px-4">
        <div className="mx-auto max-w-md text-center">
          <h1 className="text-2xl font-semibold tracking-tight">Could not load your account</h1>
          <p className="mt-1 text-sm text-muted-foreground">{result.reason} Try again in a moment.</p>
          <Link href="/adminPanel" className="mt-4 inline-block text-sm underline">
            Try again
          </Link>
        </div>
      </div>
    );
  }

  const { session } = result;
  if (!hasAnyPermission(session)) {
    return <AccessDenied fullPage title="No admin access" message="This account has no admin access." />;
  }

  // The sidebar component stores its open state in this cookie; reading it here avoids a flash on load.
  const defaultOpen = (await cookies()).get("sidebar_state")?.value !== "false";
  const items = visibleSections(session.permissions).map(({ href, label, icon }) => ({ href, label, icon }));
  const name = `${session.firstName} ${session.lastName}`.trim() || session.email;

  return (
    <TooltipProvider>
      <SidebarProvider defaultOpen={defaultOpen}>
        <AdminSidebar items={items} user={{ name, email: session.email }} />
        <SidebarInset>
          <header className="sticky top-0 z-10 flex h-12 items-center gap-2 border-b bg-background/95 px-4 backdrop-blur">
            <SidebarTrigger />
            <span className="text-sm text-muted-foreground">Admin panel</span>
          </header>
          <div className="flex-1 px-4 py-6 sm:px-6 lg:px-8">{children}</div>
        </SidebarInset>
      </SidebarProvider>
    </TooltipProvider>
  );
}
