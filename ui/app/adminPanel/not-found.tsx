import Link from "next/link";

export default function AdminNotFound() {
  return (
    <div className="mx-auto max-w-md py-20 text-center">
      <h1 className="text-2xl font-semibold tracking-tight">Not found</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        This item does not exist, or it was removed.
      </p>
      <Link href="/adminPanel" className="mt-4 inline-block text-sm underline">
        Back to the dashboard
      </Link>
    </div>
  );
}
