"use client";

import { useActionState, useId } from "react";
import { FieldError, FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formKey, IDLE, type FormState } from "@/lib/admin/forms";

type Action = (state: FormState, formData: FormData) => Promise<FormState>;

function useForm(action: Action) {
  const [state, formAction, pending] = useActionState(action, IDLE);
  const id = useId();
  const value = (name: string, initial = "") => (state.status === "error" ? state.values?.[name] : undefined) ?? initial;
  const invalid = (name: string) =>
    state.fieldErrors?.[name]?.length ? { "aria-invalid": true, "aria-describedby": `${name}-error` } : {};
  return { state, formAction, pending, id, value, invalid };
}

function Field({
  ctl: form,
  name,
  label,
  hint,
  initial,
  ...input
}: {
  ctl: ReturnType<typeof useForm>;
  name: string;
  label: string;
  hint?: string;
  initial?: string;
} & React.ComponentProps<"input">) {
  return (
    <div className="space-y-1.5">
      <Label htmlFor={`${form.id}-${name}`}>{label}</Label>
      <Input id={`${form.id}-${name}`} name={name} defaultValue={form.value(name, initial)} {...form.invalid(name)} {...input} />
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
      <FieldError name={name} state={form.state} />
    </div>
  );
}

function RolePicker({ roles, checked }: { roles: string[]; checked: string[] }) {
  return (
    <fieldset className="space-y-1.5">
      <legend className="text-sm font-medium">Roles</legend>
      <div className="flex flex-wrap gap-x-4 gap-y-1">
        {roles.map((role) => (
          <label key={role} className="flex items-center gap-1.5 text-sm">
            <input type="checkbox" name="roles" value={role} defaultChecked={checked.includes(role)} className="size-4 accent-primary" />
            {role}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/** New account. An empty password makes it an invite: the user gets an email to set one. */
export function CreateUserForm({ action, roles }: { action: Action; roles: string[] | null }) {
  const form = useForm(action);
  return (
    <form key={formKey(form.state)} action={form.formAction} className="max-w-2xl space-y-4" aria-label="New user">
      <Field ctl={form} name="email" label="Email" type="email" required maxLength={256} />
      <div className="grid gap-4 sm:grid-cols-2">
        <Field ctl={form} name="firstName" label="First name" required maxLength={100} />
        <Field ctl={form} name="lastName" label="Last name" required maxLength={100} />
      </div>
      <Field ctl={form} name="phoneNumber" label="Phone (optional)" maxLength={32} />
      <Field
        ctl={form}
        name="password"
        label="Password (optional)"
        type="password"
        autoComplete="new-password"
        maxLength={128}
        hint="Leave empty to invite: the account gets no password and the user is emailed a link to set one."
      />
      {roles ? (
        <>
          <input type="hidden" name="rolesShown" value="1" />
          <RolePicker roles={roles} checked={["User"]} />
        </>
      ) : (
        <p className="text-xs text-muted-foreground">The role list is unavailable; the account gets the User role.</p>
      )}
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="emailConfirmed" className="size-4 accent-primary" />
        Mark the email as confirmed
      </label>
      <FormMessage state={form.state} />
      <Button type="submit" disabled={form.pending}>
        {form.pending ? "Creating…" : "Create user"}
      </Button>
    </form>
  );
}

export function ProfileForm({
  action,
  initial,
}: {
  action: Action;
  initial: { firstName: string; lastName: string; phoneNumber: string; profilePictureUrl: string };
}) {
  const form = useForm(action);
  return (
    <form key={formKey(form.state)} action={form.formAction} className="space-y-4" aria-label="Edit profile">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field ctl={form} name="firstName" label="First name" required maxLength={100} initial={initial.firstName} />
        <Field ctl={form} name="lastName" label="Last name" required maxLength={100} initial={initial.lastName} />
      </div>
      <Field ctl={form} name="phoneNumber" label="Phone" maxLength={32} initial={initial.phoneNumber} hint="Empty removes it." />
      <Field
        ctl={form}
        name="profilePictureUrl"
        label="Profile picture URL"
        type="url"
        maxLength={500}
        initial={initial.profilePictureUrl}
        hint="Empty removes it."
      />
      <FormMessage state={form.state} />
      <Button type="submit" variant="outline" disabled={form.pending}>
        {form.pending ? "Saving…" : "Save profile"}
      </Button>
    </form>
  );
}

/** The user name changes with the email. Unconfirming the address signs the user out everywhere. */
export function EmailForm({ action, email }: { action: Action; email: string }) {
  const form = useForm(action);
  return (
    <form key={formKey(form.state)} action={form.formAction} className="space-y-3" aria-label="Change email">
      <Field ctl={form} name="email" label="New email" type="email" required maxLength={256} initial={email} />
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" name="markConfirmed" className="size-4 accent-primary" />
        Mark the new address as confirmed
      </label>
      <p className="text-xs text-muted-foreground">
        Left unticked, the address becomes unconfirmed and the user is signed out everywhere.
      </p>
      <FormMessage state={form.state} />
      <Button type="submit" variant="outline" disabled={form.pending}>
        {form.pending ? "Saving…" : "Change email"}
      </Button>
    </form>
  );
}

/** Replaces the whole set; none ticked removes every role. Only Admin grants permissions today. */
export function RolesForm({ action, roles, current }: { action: Action; roles: string[]; current: string[] }) {
  const form = useForm(action);
  return (
    <form key={formKey(form.state)} action={form.formAction} className="space-y-3" aria-label="Edit roles">
      <RolePicker roles={roles} checked={current} />
      <p className="text-xs text-muted-foreground">New tokens carry the change at once; issued ones keep the old roles until they expire.</p>
      <FormMessage state={form.state} />
      <Button type="submit" variant="outline" disabled={form.pending}>
        {form.pending ? "Saving…" : "Save roles"}
      </Button>
    </form>
  );
}

/** A lock until a moment in UTC; sessions are not revoked (deactivate for that). */
export function LockForm({ action }: { action: Action }) {
  const form = useForm(action);
  return (
    <form key={formKey(form.state)} action={form.formAction} className="space-y-3" aria-label="Lock account">
      <Field ctl={form} name="until" label="Locked until (UTC)" type="datetime-local" required />
      <Field ctl={form} name="reason" label="Reason (optional)" maxLength={250} hint="Kept in the audit trail." />
      <FormMessage state={form.state} />
      <Button type="submit" variant="outline" disabled={form.pending}>
        {form.pending ? "Locking…" : "Lock"}
      </Button>
    </form>
  );
}
