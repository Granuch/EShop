import Link from "next/link";

export default function RoleNotFound() {
  return (
    <div className="mx-auto max-w-md py-20 text-center">
      <h1 className="text-2xl font-semibold tracking-tight">Role not found</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        It does not exist, or it was deleted.
      </p>
      <Link href="/adminPanel/roles" className="mt-4 inline-block text-sm underline">
        Back to roles
      </Link>
    </div>
  );
}
