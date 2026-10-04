import Link from "next/link";
import AccessDenied from "@/components/Admin/accessDenied";
import PageHeader from "@/components/Admin/pageHeader";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { listNotificationTemplates } from "@/lib/admin/notification";
import { testSendAction } from "../actions";
import { NOTIFICATIONS_PATH } from "../filters";
import { TestSendForm } from "../notificationForms";

export const metadata = { title: "Templates · Notifications · Admin · EShop" };

export default async function TemplatesPage() {
  const session = await getAdminSession();
  if (!hasPermission(session, "notifications.read")) return <AccessDenied />;

  const templates = await listNotificationTemplates();
  const canManage = hasPermission(session, "notifications.manage");

  return (
    <div className="space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href="/adminPanel" />}>Dashboard</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbLink render={<Link href={NOTIFICATIONS_PATH} />}>Notifications</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>Templates</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <PageHeader
        title="Email templates"
        description="A test send is the real email with sample data (order 00000000-…, 123.45 USD), through the real mail server. It is not journalled."
      />

      <div className="grid gap-4 lg:grid-cols-2">
        {templates.map((template) => (
          <Card key={template.name}>
            <CardHeader>
              <CardTitle className="font-mono text-sm">{template.name}</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <p className="text-xs text-muted-foreground">
                {`Sent for ${template.eventType}. ${template.resendable ? "Resendable." : "Never resent: its link carries a live token."}`}
              </p>
              {canManage && <TestSendForm action={testSendAction.bind(null, template.name)} templateName={template.name} />}
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
