import Link from "next/link";

export default function UserNotFound() {
  return (
    <div className="mx-auto max-w-md py-20 text-center">
      <h1 className="text-2xl font-semibold tracking-tight">User not found</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        No account has this id. Deleted accounts are listed under State: Deleted.
      </p>
      <Link href="/adminPanel/users" className="mt-4 inline-block text-sm underline">
        Back to users
      </Link>
    </div>
  );
}
