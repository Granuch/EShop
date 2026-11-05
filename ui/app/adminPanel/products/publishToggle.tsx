"use client";

import { useActionState } from "react";
import { Button } from "@/components/ui/button";
import { IDLE, type FormState } from "@/lib/admin/forms";

type PublishToggleProps = {
  published: boolean;
  /** setPublishedAction bound to the product id and the target state. */
  action: () => Promise<FormState>;
};

/** No confirmation: both directions are idempotent and reversible (PLAN §3.6). */
function PublishToggle({ published, action }: PublishToggleProps) {
  const [state, formAction, pending] = useActionState(action, IDLE);

  return (
    <form action={formAction} className="flex items-center gap-2">
      {state.status === "error" && (
        <p role="alert" className="max-w-xs text-xs text-destructive">
          {state.message}
        </p>
      )}
      <Button type="submit" variant={published ? "outline" : "default"} disabled={pending}>
        {pending ? "Saving…" : published ? "Unpublish" : "Publish"}
      </Button>
    </form>
  );
}

export default PublishToggle;
