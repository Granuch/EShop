import Link from "next/link";
import { ShieldAlert } from "lucide-react";

type AccessDeniedProps = {
  title?: string;
  message?: string;
  /** Extra explanation, e.g. a service that still requires the Admin role (lib/admin/permissions.ts). */
  hint?: string;
  /** The whole-page variant for an account with no admin access at all: links back to the shop. */
  fullPage?: boolean;
};

function AccessDenied({
  title = "Access denied",
  message = "Your account does not have permission to view this section.",
  hint,
  fullPage = false,
}: AccessDeniedProps) {
  return (
    <div className={fullPage ? "flex min-h-svh items-center justify-center px-4" : "px-4 py-20 sm:px-6 lg:px-8"}>
      <div className="mx-auto max-w-md text-center">
        <ShieldAlert aria-hidden className="mx-auto size-10 text-muted-foreground" />
        <h1 className="mt-4 text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{message}</p>
        {hint && <p className="mt-2 text-sm text-muted-foreground">{hint}</p>}
        <Link href={fullPage ? "/" : "/adminPanel"} className="mt-4 inline-block text-sm underline">
          {fullPage ? "Back to the shop" : "Back to the dashboard"}
        </Link>
      </div>
    </div>
  );
}

export default AccessDenied;
