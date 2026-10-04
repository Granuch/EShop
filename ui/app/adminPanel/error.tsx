"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";

/**
 * Catches errors from admin pages (not from adminPanel/layout.tsx, which never throws). In production a server
 * error's message is replaced by a generic one, so the digest is what support can match against the server log.
 */
export default function AdminError({ error, retry }: { error: Error & { digest?: string }; retry: () => void }) {
  return (
    <div role="alert" className="mx-auto max-w-md py-20 text-center">
      <h1 className="text-2xl font-semibold tracking-tight">Something went wrong</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        The page could not be loaded. A service may be unavailable; trying again usually helps.
      </p>
      {error.digest && (
        <p className="mt-2 text-xs text-muted-foreground">
          Reference: <code className="font-mono">{error.digest}</code>
        </p>
      )}
      <div className="mt-4 flex items-center justify-center gap-4">
        <Button onClick={() => retry()}>Try again</Button>
        <Link href="/adminPanel" className="text-sm underline">
          Back to the dashboard
        </Link>
      </div>
    </div>
  );
}
