"use client";

import { useActionState } from "react";
import { FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { IDLE } from "@/lib/admin/forms";
import { replayWebhooksAction, type ReplayState } from "./actions";

/** One press replays every outstanding capture (up to 100); press again while `outstanding` is above 0. */
function ReplayButton() {
  const [state, formAction, pending] = useActionState<ReplayState, FormData>(replayWebhooksAction, IDLE);
  const failed = state.report?.results.filter((result) => result.outcome === "Failed") ?? [];

  return (
    <form action={formAction} className="space-y-3" aria-label="Replay failed webhooks">
      <Button type="submit" variant="outline" disabled={pending}>
        {pending ? "Replaying…" : "Replay failed Stripe webhooks"}
      </Button>
      <FormMessage state={state} />
      {failed.length > 0 && (
        <ul className="space-y-1 text-xs">
          {failed.map((result) => (
            // The raw exception text: operator diagnostics only (F-54).
            <li key={result.id} className="font-mono [overflow-wrap:anywhere]">
              {`${result.eventType ?? "?"} ${result.stripeEventId ?? result.id}: ${result.error ?? "failed"}`}
            </li>
          ))}
        </ul>
      )}
    </form>
  );
}

export default ReplayButton;
