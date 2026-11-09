/**
 * Results of the account actions that flip the state the Account card shows (its button is replaced, so the button's
 * own message would vanish). Those actions redirect back with `?done=<key>` and the page shows this text.
 */
export const DONE_NOTICES = {
  activate: "Account activated.",
  deactivate: "Account deactivated; every session was signed out.",
  restore: "Account restored. It is deactivated: activate it to let the user sign in.",
  unlock: "Unlocked; failed sign-in attempts were cleared too.",
  delete: "Account deleted. It stays readable here and can be restored; its email stays taken.",
  lock: "Account locked. Existing sessions were not signed out.",
  "confirm-email": "Email marked as confirmed.",
  "disable-2fa": "Two-factor authentication turned off; the user can set it up again.",
} as const;

export type DoneNotice = keyof typeof DONE_NOTICES;

export function isDoneNotice(value: unknown): value is DoneNotice {
  return typeof value === "string" && value in DONE_NOTICES;
}
