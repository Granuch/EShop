import Link from "next/link";

export default function NotificationNotFound() {
  return (
    <div className="mx-auto max-w-md py-20 text-center">
      <h1 className="text-2xl font-semibold tracking-tight">Notification not found</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        It does not exist, or it is older than 90 days.
      </p>
      <Link href="/adminPanel/notifications" className="mt-4 inline-block text-sm underline">
        Back to notifications
      </Link>
    </div>
  );
}
