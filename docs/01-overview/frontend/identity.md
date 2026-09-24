# Identity API

Accounts, sign-in, tokens, two-factor authentication and the user's own profile, plus the admin user and role
management screens.

**Verified at:** `1fcb630` (`feature/admin-panel`, 2026-09-23). Every endpoint was checked against the C# source and
the service's OpenAPI document, and called through the gateway on the compose `sandbox` stack. Shared rules (errors,
paging, rate limits, CORS) are in [conventions.md](conventions.md) and are not repeated here.

## Base paths through the gateway

| Path | Gateway policy | Audience |
|---|---|---|
| `/api/v1/auth/**` | anonymous | Storefront |
| `/api/v1/account/**` | signed in | Storefront |
| `/api/v1/admin/users/**` | `Admin` role | Admin panel |
| `/api/v1/roles/**` | `Admin` role | Admin panel |

Not routed through the gateway: `GET /api/v1/users/{userId}/contact` and Identity's own `GET /api/v1/admin/audit`
(see [Not callable by clients](#not-callable-by-clients)).

**Identity differs from the other services in four ways.** Read these once before using any endpoint below:

- **Error bodies have no `type` or `title`**, and `traceId` has the form `0HNOPJ7JU6J6S:00000002`
  ([conventions.md §3.1](conventions.md#31-the-envelope), F-21).
- **Validation errors mostly use shape (a), `Validation.Failed`**, with every message in `detail`. The MVC shape (c)
  appears when the body cannot be bound: malformed JSON, explicit `null` on a required string, or a query value of the
  wrong type ([conventions.md §3.3](conventions.md#33-validation-errors-three-shapes)).
- **User ids are strings with no route constraint.** A malformed id such as `/admin/users/not-a-guid` reaches the
  service and gets a problem+json `User.NotFound` (404), not the bare 404 that other services give.
- **The OpenAPI document lists PascalCase paths** (`/api/v1/Auth/login`). Call the lowercase paths used here (F-19).

## Contents

- [Storefront / customer](#storefront--customer)
  - [Sign-up and sign-in](#sign-up-and-sign-in): register, login, refresh, revoke, confirm email, forgot and reset
    password
  - [Own account](#own-account): profile, change password, two-factor authentication
  - [Failed logins and lockout](#failed-logins-and-lockout)
- [Admin panel](#admin-panel)
  - [Users](#users-apiv1adminusers)
  - [Roles](#roles-apiv1roles)
  - [Audit trail](#audit-trail)
- [Not callable by clients](#not-callable-by-clients)
- [Types](#types)
- [Frontend notes](#frontend-notes)

---

## Storefront / customer

### Sign-up and sign-in

All seven `/auth` endpoints are anonymous at both layers. A token sent with them is ignored.

| Endpoint | Rate limit per client IP | Minimum response time |
|---|---|---|
| `POST /auth/register` | `auth`, 10 / 60 s | 0.8–1.2 s |
| `POST /auth/login` | `login`, 5 / 60 s | 0.8–1.2 s |
| `POST /auth/refresh-token` | `auth`, 10 / 60 s | 0.8–1.2 s |
| `POST /auth/revoke-token` | `auth`, 10 / 60 s | none |
| `POST /auth/confirm-email` | `auth`, 10 / 60 s | 0.8–1.2 s |
| `POST /auth/forgot-password` | `login`, 5 / 60 s | 0.8–1.2 s |
| `POST /auth/reset-password` | `login`, 5 / 60 s | 0.8–1.2 s |

- **The buckets are shared.** Endpoints with the same policy spend one allowance: `register`, `refresh-token`,
  `revoke-token` and `confirm-email` together get 10 calls a minute per IP, and `login`, `forgot-password` and
  `reset-password` together get 5.
- **429 bodies.** A 429 here is problem+json with `errorCode` `Request.RateLimited` and `Retry-After: 60`
  ([conventions.md §7](conventions.md#7-rate-limits)).
- **Padding.** The six padded endpoints never answer faster than about 0.8 s, even for a validation error. This hides
  whether an account exists. Show a spinner rather than treating the delay as a fault.

#### `POST /api/v1/auth/register`

Creates a customer account with the `User` role. Source: `RegisterCommand`.

**Body** ([`RegisterRequest`](#registerrequest)):

| Field | Type | Rules |
|---|---|---|
| `email` | string | Required, an email address, at most 256 characters. Stored as sent, and matched case-insensitively |
| `password` | string | Required, 8–100 characters, with at least one uppercase letter, one lowercase letter, one digit and one other character |
| `firstName` | string | Required, at most 50 characters. Letters in any script, spaces, `'` and `-` only |
| `lastName` | string | Same as `firstName` |

**200** [`RegisterResponse`](#registerresponse):

```json
{"userId":"afc10747-33cb-491d-bf78-7de7ed9da476","email":"fe-contracts-s2a@example.com",
 "message":"Registration successful. Please check your email to confirm."}
```

The account can log in immediately.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | A rule above fails. All messages are joined in `detail` |
| 400 | `ValidationError` | Shape (c): the body is not JSON (key `$` and `command`), or a field is `null` (key `Email`, …) |
| 400 | `Auth.EmailExists` | The email is taken, in any letter case |
| 400 | `Auth.CreateFailed` | ASP.NET Identity refused the account; `detail` lists its reasons. From source, not observed: the checks above run first |
| 429 | `Request.RateLimited` | `auth` bucket spent |

> ⚠ The `message` promises a confirmation email, but **none is sent**. Do not show it, and do not add a "confirm your
> email" step to sign-up. (F-27)

#### `POST /api/v1/auth/login`

Checks the credentials and returns a token pair. Source: `LoginCommand`.

**Body** ([`LoginRequest`](#loginrequest)): `email` (required, an email address), `password` (required), and
`twoFactorCode` (optional: exactly 6 digits, only when the server asked for it).

**200** is one of two shapes ([`LoginResponse`](#loginresponse)):

1. **Signed in.**

   ```json
   {"accessToken":"eyJhbGciOi…","refreshToken":"FfbrHl18…","expiresIn":3600,"tokenType":"Bearer","requires2FA":false,
    "user":{"id":"afc10747-33cb-491d-bf78-7de7ed9da476","email":"fe-contracts-s2a@example.com","firstName":"José",
    "lastName":"O'Brien-Müller","roles":["User"]}}
   ```

2. **Second factor needed.** The password was right and the account has 2FA on, but the request had no
   `twoFactorCode`:

   ```json
   {"accessToken":"","refreshToken":"","expiresIn":0,"tokenType":"Bearer","requires2FA":true,"user":null}
   ```

   Ask for the authenticator code, then **send the same request again** with `twoFactorCode` added.

**Branch on `requires2FA`**, never on the status code. Both shapes are 200.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | Missing email or password, a malformed email, or a `twoFactorCode` that is not 6 digits |
| 400 | `ValidationError` | Shape (c): the body is not JSON |
| 401 | `Auth.InvalidCredentials` | `detail` `"Invalid email or password"`. Wrong password, unknown email, **and also** a deactivated, deleted or admin-locked account. The response never says which |
| 401 | `Auth.Invalid2FA` | `twoFactorCode` was sent and is wrong |
| 401 | `Auth.TooManyAttempts` | The account or the client IP is blocked after failed logins. See [Failed logins and lockout](#failed-logins-and-lockout) |
| 429 | `Request.RateLimited` | `login` bucket spent |

- **Side effects.** A successful login records `lastLoginAt` and the client IP, and resets the account's failed-login
  counter. Refusals count as failed attempts, as described in [Failed logins and lockout](#failed-logins-and-lockout).
- **Roles.** Use `user.roles` to decide what to show, not the JWT ([conventions.md §4](conventions.md#claims-and-roles)).

#### `POST /api/v1/auth/refresh-token`

Exchanges a refresh token for a new pair. Source: `RefreshTokenCommand`.

**Body** ([`RefreshTokenRequest`](#refreshtokenrequest)): `{ "refreshToken": "…" }`, required.

**200** [`RefreshTokenResponse`](#refreshtokenresponse): `{ "accessToken": "…", "refreshToken": "…", "expiresIn": 3600 }`.
Unlike login, it carries no `user` and no `tokenType`.

**The old refresh token is dead from this point.** Store the new one before anything else. Run one refresh at a time
([conventions.md §4](conventions.md#tokens)).

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `refreshToken` is empty |
| 401 | `Auth.InvalidToken` | Unknown, expired, revoked or already rotated. This includes every token of a user whose password was changed or reset, or who was deactivated, deleted or signed out by an admin |
| 401 | `Auth.TokenAlreadyUsed` | Lost a race with a parallel refresh of the same token. From source, not observed |
| 401 | `Auth.AccountDisabled` | The account was deactivated while the token stayed valid. From source, not observed: deactivating also revokes every token, so the client sees `Auth.InvalidToken` instead |
| 429 | `Request.RateLimited` | `auth` bucket spent |

Treat every 401 here the same way: sign the user out.

#### `POST /api/v1/auth/revoke-token`

Signs out one session. Source: `RevokeTokenCommand`.

**Body** ([`RevokeTokenRequest`](#revoketokenrequest)): `{ "refreshToken": "…" }`, required.

**204**, no body. The call is idempotent: an unknown, already revoked or already rotated token also gets 204. The
access token stays valid until it expires, so drop it on the client too.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `refreshToken` is empty |
| 429 | `Request.RateLimited` | `auth` bucket spent |

#### `POST /api/v1/auth/confirm-email`

Marks an email address confirmed with a token. Source: `ConfirmEmailCommand`.

> ⚠ **No user can call this successfully today.** Registration never delivers a token (F-27), so a client has nothing
> to send. Admins confirm addresses with [`POST /admin/users/{id}/confirm-email`](#post-apiv1adminusersidconfirm-email)
> instead. Build no UI for it until F-27 is resolved.

**Body** ([`ConfirmEmailRequest`](#confirmemailrequest)): `userId` and `token`, both required.

**200** [`SuccessMessageResponse`](#successmessageresponse): `{"success":true,"message":"Email confirmed successfully"}`,
or `"Email already confirmed"`.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `userId` or `token` is empty |
| 400 | `Auth.UserNotFound` | No user has this id |
| 400 | `Auth.InvalidToken` | The token is wrong or expired |
| 429 | `Request.RateLimited` | `auth` bucket spent |

> ⚠ For an address that is already confirmed, the answer is 200 `"Email already confirmed"` whatever the token. The
> 400 `Auth.UserNotFound` also tells a caller whether a user id exists. (F-30)

#### `POST /api/v1/auth/forgot-password`

Emails a password-reset link. Source: `ForgotPasswordCommand`.

**Body** ([`ForgotPasswordRequest`](#forgotpasswordrequest)): `{ "email": "…" }`, required, an email address, at most
256 characters.

**200** [`SuccessMessageResponse`](#successmessageresponse), **always the same**, whether or not the address belongs to
an account:

```json
{"success":true,"message":"If the email exists, a password reset link has been sent"}
```

Show that message as it is. No email goes to an unknown or deactivated address.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | The email is missing or malformed |
| 429 | `Request.RateLimited` | `login` bucket spent |

**The reset link.** The email (sent by the Notification service a few seconds later) links to:

```
<PasswordReset:ResetUrlBase>?userId=<user id>&token=<reset token>
```

Both values are URL-encoded. The compose default for the base is `http://localhost:3000/reset-password`
(`PASSWORD_RESET_URL_BASE` in `.env`). **The frontend must serve that page.** It reads `userId` and `token` with
`URLSearchParams`, which decodes them, asks for the new password, and calls
[`reset-password`](#post-apiv1authreset-password). The same page also completes an admin-created account (see
[`POST /admin/users`](#post-apiv1adminusers)).

#### `POST /api/v1/auth/reset-password`

Sets a new password with the token from the email. Source: `ResetPasswordCommand`.

**Body** ([`ResetPasswordRequest`](#resetpasswordrequest)):

| Field | Type | Rules |
|---|---|---|
| `userId` | string | Required, from the link |
| `token` | string | Required, from the link, **decoded** |
| `newPassword` | string | Required, at least 8 characters, with the same four character classes as registration. No maximum is checked here |

**200** [`SuccessMessageResponse`](#successmessageresponse):
`{"success":true,"message":"Password has been reset successfully"}`.

- **Signs out everywhere.** Every refresh token of the user is revoked in the same transaction. Access tokens already
  issued keep working until they expire.
- **Single use.** A reset token works once; the second use gets `Auth.ResetFailed`.
- **It does not sign in**, and it does not clear a failed-login block ([below](#failed-logins-and-lockout)).

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | A field is missing, or the password is too weak |
| 400 | `Auth.UserNotFound` | Unknown `userId`. `detail` is `"Invalid password reset request"` |
| 400 | `Auth.AccountDisabled` | The account is deactivated. From source, not observed |
| 400 | `Auth.ResetFailed` | The token is wrong, expired or already used (`detail` `"Invalid token."`), or ASP.NET Identity rejected the password |
| 429 | `Request.RateLimited` | `login` bucket spent |

### Own account

All six endpoints need a signed-in user: gateway **signed in** · service **signed in**. There is no named rate limit,
only the global one. They act on the user in the token; no id is passed.

For a token whose account has since been deleted or deactivated, they answer with the codes in
[the account-state table](#when-the-account-is-no-longer-usable) below.

#### `GET /api/v1/account/profile`

**200** [`UserProfile`](#userprofile):

```json
{"id":"afc10747-33cb-491d-bf78-7de7ed9da476","email":"fe-contracts-s2a@example.com","firstName":"José",
 "lastName":"O'Brien-Müller","profilePictureUrl":null,"emailConfirmed":false,"twoFactorEnabled":false,"isActive":true,
 "createdAt":"2026-09-23T17:51:11.674841Z","lastLoginAt":"2026-09-23T17:51:49.314042Z","roles":["User"]}
```

- **Cached for 5 minutes.** A profile edit and a 2FA change clear the cache. A login does not, so `lastLoginAt` can lag
  by up to 5 minutes.

| Status | `errorCode` | When |
|---|---|---|
| 401 | — (empty body) | No token or an invalid one; the gateway answers |
| 404 | `Account.NotFound` | The account was deleted after the token was issued |
| 404 | `Auth.AccountDisabled` | The account was deactivated after the token was issued (F-34) |

#### `PUT /api/v1/account/profile`

Changes the name and picture. Source: `UpdateProfileRequest` → `UpdateProfileCommand`.

**Body** ([`UpdateProfileRequest`](#updateprofilerequest)):

| Field | Type | Rules |
|---|---|---|
| `firstName` | string | **Required** on every call, with the registration rules. It is not a partial update |
| `lastName` | string | Same |
| `profilePictureUrl` | string \| null | Optional; see below |

`profilePictureUrl` has three cases (all observed):

| Sent | Effect |
|---|---|
| omitted, or `null` | The stored picture is **kept** |
| `""` (the empty string) | The picture is **removed** |
| an absolute `http`/`https` URL, at most 500 characters | The picture is **replaced** |

A whitespace-only value such as `"  "` is **rejected** with 400, not treated as "remove". The API stores the URL only;
it does not upload images.

**200** [`MessageResponse`](#messageresponse): `{"message":"Profile updated successfully"}`. Re-read the profile to show
the result; the response does not echo it.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | A name is missing, too long or has a disallowed character; the URL is not http(s), too long, or blank |
| 400 | `ValidationError` | Shape (c): the body is not JSON, or `firstName`/`lastName` is `null` (key `FirstName`) |
| 400 | `Account.NotFound` | The account was deleted |
| 400 | `Account.UpdateFailed` | ASP.NET Identity refused the save. From source, not observed |

#### `POST /api/v1/account/change-password`

**Body** ([`ChangePasswordRequest`](#changepasswordrequest)): `currentPassword` (required) and `newPassword` (required,
8–100 characters, the four character classes, and different from `currentPassword`).

**200** [`MessageResponse`](#messageresponse): `{"message":"Password changed successfully"}`.

> ⚠ **This signs the caller out too.** Every refresh token of the user is revoked, **including the one this client
> holds**. The current access token keeps working until it expires, but the next refresh fails with
> `Auth.InvalidToken`. After a 200, log in again with the new password straight away. (Behaviour by design.)

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | A field is missing, the new password is too weak, or it equals the current one |
| 400 | `Account.PasswordChangeFailed` | `detail` `"Incorrect password."`: the current password is wrong. It is **not** a 401, and it does not count as a failed login |
| 400 | `Account.NotFound` / `Auth.AccountDisabled` | The account was deleted or deactivated |

#### Two-factor authentication

2FA uses an authenticator app (TOTP: 6 digits, 30 s). Turning it on takes two calls:

1. `enable-2fa` returns the secret.
2. `verify-2fa`, with a code from the app, switches 2FA on.

After that, login needs the code ([login](#post-apiv1authlogin)).

##### `POST /api/v1/account/enable-2fa`

No body. **200** [`Enable2FAResponse`](#enable2faresponse):

```json
{"sharedKey":"ush3 t35w p4hu aqrk vglx mtcq vhbr rxpl",
 "qrCodeUri":"otpauth://totp/EShop:fe-contracts-s2a@example.com?secret=USH3T35WP4HUAQRKVGLXMTCQVHBRRXPL&issuer=EShop&digits=6",
 "message":"Scan the QR code with your authenticator app, then verify with a code"}
```

- **Rendering.** `qrCodeUri` is a URI, not an image: render it as a QR code on the client (any QR library). Show
  `sharedKey` for manual entry.
- **Nothing changes yet.** 2FA stays off until `verify-2fa` succeeds. Calling `enable-2fa` again before that returns
  **the same key**. After 2FA has been turned off, the next `enable-2fa` issues a new key.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Account.2FAAlreadyEnabled` | 2FA is already on |
| 400 | `Account.UserNotFound` | The account was deleted (note the different code from the profile endpoints, F-34) |
| 400 | `Account.2FAError` | No key could be generated. From source, not observed |

##### `POST /api/v1/account/verify-2fa`

**Body** ([`TwoFactorCodeRequest`](#twofactorcoderequest)): `{ "code": "123456" }`, exactly 6 digits.

**200** [`Verify2FAResponse`](#verify2faresponse):

```json
{"success":true,"recoveryCodes":["R84W6-KX6BP","J7G5F-JTC9M","…8 more"],
 "message":"Two-factor authentication has been enabled. Save your recovery codes in a safe place."}
```

2FA is now on, and the next login will answer `requires2FA: true`.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `code` is not exactly 6 digits |
| 400 | `Account.InvalidCode` | The code is wrong, or no key was issued yet |
| 400 | `Account.UserNotFound` | The account was deleted |

> ⚠ **Recovery codes cannot be used.** Ten codes are returned, but no endpoint accepts one. Login takes only a 6-digit
> `twoFactorCode`, and a recovery code there fails validation (observed). A user who loses the authenticator must ask
> an admin to run [`disable-2fa`](#post-apiv1adminusersiddisable-2fa). Do not tell users the codes will let them in.
> (F-29)

##### `POST /api/v1/account/disable-2fa`

**Body** ([`TwoFactorCodeRequest`](#twofactorcoderequest)): a current code from the app, exactly 6 digits.

**200** [`SuccessMessageResponse`](#successmessageresponse):
`{"success":true,"message":"Two-factor authentication has been disabled"}`. The secret is discarded; turning 2FA back
on starts from a new key.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `code` is not exactly 6 digits |
| 400 | `Account.2FANotEnabled` | 2FA is off |
| 400 | `Account.InvalidCode` | The code is wrong |
| 400 | `Account.UserNotFound` | The account was deleted |

#### When the account is no longer usable

An access token outlives changes an admin makes to the account. The account endpoints then answer as follows (all
observed):

| Account state | `GET profile` | `PUT profile`, `change-password` | 2FA endpoints | login | refresh |
|---|---|---|---|---|---|
| Deleted | 404 `Account.NotFound` | 400 `Account.NotFound` | 400 `Account.UserNotFound` | 401 `Auth.InvalidCredentials` | 401 `Auth.InvalidToken` |
| Deactivated | 404 `Auth.AccountDisabled` | 400 `Auth.AccountDisabled` (`change-password`) | from source: not checked | 401 `Auth.InvalidCredentials` | 401 `Auth.InvalidToken` |

Treat any of these as "sign the user out". (F-34)

### Failed logins and lockout

Separately from the rate limits, login tracks failed attempts per account (by email) and per client IP. The counters
live for **15 minutes after the last failure**.

| Failures | Effect on the next login |
|---|---|
| 3 for one email | **Every** login for that email is refused with 401 `Auth.TooManyAttempts`, **including one with the right password**, until 15 minutes pass with no new failure. `detail` says `"Too many failed attempts. Please wait 2 seconds before trying again"` |
| 10 from one IP, across several emails | Every login from that IP is refused for 30 minutes, with `detail` `"This IP address has been temporarily blocked…"` (from source) |

The source also has a 10-minute lock after 5 failures and a rule for failures from 5 different IPs. In practice
neither is reached: after the third failure, the block above refuses logins before they are counted.

- **Who can clear it.** A successful login clears an account's counter, but a blocked account cannot reach one. The
  admin action [`unlock`](#post-apiv1adminusersidunlock) clears it (observed). A password reset does not.
- **What counts.** A wrong password, an unknown email, a disabled or locked account, and a wrong 2FA code all count.
  Validation errors and refusals by the block itself do not.

> ⚠ **Ignore the number of seconds in `detail`.** It is always the delay for the third failure, and waiting it out
> changes nothing: observed, the right password 5 s later was still refused, and an account failed by an earlier probe
> was still refused 11 minutes later. On `Auth.TooManyAttempts`, tell the user to try again later or reset the password.
> Do not start a countdown. Because unknown emails count too, anyone can block any account this way for 15 minutes.
> (F-28)

---

## Admin panel

### Users (`/api/v1/admin/users`)

**Auth, both layers:**

| Endpoints | Gateway | Service |
|---|---|---|
| every `GET` | `Admin` role | `users.read` |
| every write except roles | `Admin` role | `users.read` **and** `users.manage` |
| `PUT /{id}/roles` | `Admin` role | `users.read` **and** `roles.manage` |

Without a token the gateway answers 401; with a non-admin token it answers 403 itself, and the request never reaches
Identity (observed). Both have empty bodies. Identity checks the permissions again when called directly (403 observed
on port 7001).

**Common to every `/{id}` endpoint:**

- `{id}` is the user id as a string.
- An unknown or malformed id answers **404 `User.NotFound`**. So does a **deleted** user on every endpoint except the
  three reads (`GET /{id}`, `/{id}/roles`, `/{id}/sessions`), `DELETE` (which answers 404 again) and `restore`.
- Writes answer **204 with no body**, apart from `POST /admin/users` (201). Re-read the user to show the result.
- Every write, successful or rejected, is recorded in the [audit trail](#audit-trail).
- Many writes clear the user's cached profile; the user sees the change on their next profile read.
- If ASP.NET Identity refuses a save after the change was applied, the whole write is rolled back and answers **500
  `InternalServerError`**. From source, not observed.

#### `GET /api/v1/admin/users`

The paged user list. Response: [`PagedResult<AdminUser>`](conventions.md#61-pagedresultt-offset-pages-the-common-case).

**Query** ([`AdminUserListQuery`](#adminuserlistquery)); all optional:

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `search` | string | — | Case-insensitive substring of the email, user name, first name or last name. At most 256 characters |
| `role` | string | — | Role name, any case. At most 256 characters. An unknown role gives an empty page |
| `isActive` | boolean | — | Filter on the active flag |
| `isDeleted` | boolean | — | `true` lists **only** deleted users. `false` or omitted lists only live users |
| `emailConfirmed` | boolean | — | |
| `twoFactorEnabled` | boolean | — | |
| `createdFrom`, `createdTo` | date-time | — | Inclusive range on `createdAt`. **Send `Z` or an offset** (F-15) |
| `lastLoginFrom`, `lastLoginTo` | date-time | — | Inclusive range on `lastLoginAt`; users who never logged in are excluded. Same `Z` rule |
| `sortBy` | `CreatedAt` \| `Email` \| `LastLoginAt` | `CreatedAt` | Name in any case; the integers `0`/`1`/`2` also work. Ties are broken by id |
| `isDescending` | boolean | `true` | |
| `pageNumber` | integer | `1` | ≥ 1 |
| `pageSize` | integer | `20` | 1–100 |

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `pageNumber` < 1, `pageSize` outside 1–100, `search` or `role` too long, a `…From` after its `…To`. For the last case `detail` starts with `": "`, because the rule has no property name |
| 400 | `ValidationError` | Shape (c): a value of the wrong type, for example `isActive=yes` or `sortBy=Bogus` (key `SortBy`) |
| 500 | `InternalServerError` | A date without a zone, such as `createdFrom=2026-09-23` (F-15) |

> ⚠ The OpenAPI document also lists `EffectiveSortBy`, `EffectiveIsDescending`, `EffectivePageNumber` and
> `EffectivePageSize`. They are computed on the server and **ignored** if sent. It also types `sortBy` as an integer
> with no values, although names work. (F-31)

#### `GET /api/v1/admin/users/stats`

Counts for the dashboard tile. **200** [`AdminUserStats`](#adminuserstats):
`{"total":10,"newInPeriod":10,"active":9,"locked":0,"unconfirmed":8,"deleted":0}`.

**Query:** `from`, `to` (date-time, optional). They bound **only** `newInPeriod`; every other number covers all
users. `total`, `active`, `locked` and `unconfirmed` exclude deleted users, and `deleted` counts only them.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `from` is after `to` |
| 500 | `InternalServerError` | A date without a zone (F-15) |

#### `GET /api/v1/admin/users/{id}`

One user's detail card, **including a deleted user**. **200** [`AdminUserDetails`](#adminuserdetails). 404
`User.NotFound` for an unknown id.

`lockoutEnd` is a `DateTimeOffset`, sent as `"2026-09-23T18:59:25+00:00"`, **not** with `Z` (F-35). `isLockedOut` is
`lockoutEnd` compared with the server clock at read time.

#### `GET /api/v1/admin/users/{id}/roles`

The user's role names, as a bare array: `["User"]`. **Works for deleted users.** 404 `User.NotFound` for an unknown
id.

#### `GET /api/v1/admin/users/{id}/sessions`

The user's refresh-token sessions, newest first, as a bare array of [`AdminUserSession`](#adminusersession). The
list is not paged, and it includes revoked and expired sessions. An empty array means the user has never signed in.
404 `User.NotFound` for an unknown id.

```json
[{"id":"8a95b8c1-9a2d-43e3-8302-6763b91c2b74","createdAt":"2026-09-23T17:56:18.195592Z",
  "expiresAt":"2026-09-30T17:56:18.195378Z","createdByIp":"10.22.11.1","revokedAt":null,"revokedByIp":null,
  "revokeReason":null,"isActive":true},
 {"id":"ef018e33-b6f2-48e5-8363-be9536ecd565","createdAt":"2026-09-23T17:55:13.118652Z",
  "expiresAt":"2026-09-30T17:55:13.118468Z","createdByIp":"10.22.9.1","revokedAt":"2026-09-23T17:55:46.442737Z",
  "revokedByIp":null,"revokeReason":"Password changed","isActive":false}]
```

`isActive` means "not revoked and not expired". `revokeReason` is `null` while the session is live. Observed values:
`"Rotated"` (replaced by a refresh; the only reason with `revokedByIp`), `"Password changed"`, `"Password reset"`,
`"Account deactivated by an administrator"`, `"Account deleted by an administrator"` and `"Revoked by an
administrator"`. A sign-out through `revoke-token` gives `"Revoked by user"` (from source). The response never contains
a token or a token hash (checked by value against the database).

#### `POST /api/v1/admin/users`

Creates an account on a user's behalf. Source: `CreateUserCommand`.

**Body** ([`CreateUserRequest`](#createuserrequest)):

| Field | Type | Rules |
|---|---|---|
| `email` | string | Required, an email address, at most 256 characters. Surrounding spaces are trimmed |
| `firstName`, `lastName` | string | Required; the registration name rules |
| `phoneNumber` | string \| null | Optional, at most 32 characters. Not otherwise checked |
| `password` | string \| null | Optional, at most 128 characters. When given, it must pass the password policy (checked by ASP.NET Identity, so a weak one gets `User.CreateFailed`, not `Validation.Failed`) |
| `roles` | string[] \| null | Optional, at most 10 names, none blank. **Omitted means `["User"]`; `[]` means no role at all** |
| `emailConfirmed` | boolean | Optional, default `false` |

**201** [`CreateUserResponse`](#createuserresponse): `{"userId":"10c5…","email":"fe-contracts-s2v1@example.com","inviteSent":false}`.

**Without a `password`**, the account has no password and `inviteSent` is `true`. The user is emailed the ordinary
"Reset your EShop password" message, whose link goes to the same `/reset-password` page as
[forgot-password](#post-apiv1authforgot-password).

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | A rule above fails |
| 400 | `User.CreateFailed` | The password fails the policy; `detail` lists ASP.NET Identity's messages |
| 404 | `Role.NotFound` | A role in `roles` does not exist. Nothing is created |
| 409 | `User.EmailConflict` | The email is taken, in any case, **including by a deleted account** |

> ⚠ `Location` points at the internal service host (`http://identity-api:8080/api/v1/admin/users/{id}`), and a browser
> on another origin cannot read it anyway. Use `userId` from the body. (F-32, F-04)

#### `PUT /api/v1/admin/users/{id}`

Edits the profile fields. **Every field is optional**: omitted or `null` leaves it unchanged. Body
([`UpdateUserRequest`](#updateuserrequest)):

| Field | Rules | To clear it |
|---|---|---|
| `firstName`, `lastName` | The registration name rules; trimmed. A blank value is rejected | — (cannot be cleared) |
| `phoneNumber` | At most 32 characters; trimmed | send `""` |
| `profilePictureUrl` | Absolute http(s) URL, at most 500 characters. Whitespace-only is rejected | send `""` |

**204.** `{}` is accepted and changes nothing. Errors: 400 `Validation.Failed`; 404 `User.NotFound`.

#### `PUT /api/v1/admin/users/{id}/email`

Changes the email **and the user name** (they are always equal). Body
([`ChangeUserEmailRequest`](#changeuseremailrequest)): `email` (required, an email address, at most 256 characters,
trimmed) and `markConfirmed` (optional boolean).

**204.** The new address is confirmed only if `markConfirmed` is `true`; **omitting it resets `emailConfirmed` to
`false`**, even when it was confirmed before.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `email` is missing or malformed |
| 404 | `User.NotFound` | |
| 409 | `User.EmailConflict` | Another account, live or deleted, uses the address |

#### `POST /api/v1/admin/users/{id}/deactivate`

Suspends the account and **revokes every refresh token**. No body. **204**, also when it is already inactive.

The user cannot log in (`Auth.InvalidCredentials`) or refresh (`Auth.InvalidToken`). Access tokens already issued keep
working until they expire, for endpoints that do not check the account state
([table above](#when-the-account-is-no-longer-usable)).

Errors: 404 `User.NotFound`.

#### `POST /api/v1/admin/users/{id}/activate`

Re-enables a deactivated account. No body. **204**, also when it is already active. It does not undo a lock or a
deletion. Errors: 404 `User.NotFound` (including a deleted user: restore it first).

#### `DELETE /api/v1/admin/users/{id}`

Soft-deletes the account. No body. **204.**

- **What happens.** The user is also deactivated, and every refresh token is revoked.
- **Still readable.** The user disappears from the list unless you pass `isDeleted=true`, and stays readable through
  `GET /{id}`, which shows `isDeleted: true` and `deletedAt`. Roles are kept.
- **Address stays taken.** The email stays taken ([`POST`](#post-apiv1adminusers) and
  [email change](#put-apiv1adminusersidemail) answer 409).

Errors: 404 `User.NotFound`, including a second delete.

#### `POST /api/v1/admin/users/{id}/restore`

Undeletes an account. No body. **204.** The account comes back **deactivated**, with the roles it had: call
[`activate`](#post-apiv1adminusersidactivate) afterwards.

| Status | `errorCode` | When |
|---|---|---|
| 404 | `User.NotFound` | Unknown id |
| 409 | `User.NotDeleted` | The account is not deleted |

#### `POST /api/v1/admin/users/{id}/lock`

Blocks sign-in until a given moment. Body ([`LockUserRequest`](#lockuserrequest)):

| Field | Rules |
|---|---|
| `until` | **Required**, a date-time **in the future**, with `Z` or an offset. The offset is honoured |
| `reason` | Optional, at most 250 characters. Recorded only in the log and the audit payload |

**204.** While locked, login answers `Auth.InvalidCredentials`. Existing sessions are **not** revoked. For an
open-ended suspension, use [`deactivate`](#post-apiv1adminusersiddeactivate).

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `until` is missing or not in the future (`detail`: `'until' must be in the future — a past date is an unlock, not a lock`), or `reason` is too long |
| 404 | `User.NotFound` | |

#### `POST /api/v1/admin/users/{id}/unlock`

No body. **204.** It lifts an admin lock, **and** clears the failed-login block described in
[Failed logins and lockout](#failed-logins-and-lockout). Observed: a throttled account logged in right after an
unlock. Errors: 404 `User.NotFound`.

#### `POST /api/v1/admin/users/{id}/reset-password`

Emails the user a password-reset link, the same email as
[forgot-password](#post-apiv1authforgot-password). **It takes no password**; a request body is ignored. **204.**
Errors: 404 `User.NotFound`.

#### `POST /api/v1/admin/users/{id}/confirm-email`

Marks the address confirmed without a token. No body. **204**, also when it is already confirmed. Errors: 404
`User.NotFound`.

#### `POST /api/v1/admin/users/{id}/disable-2fa`

Turns off the user's 2FA and discards the secret, without a code. This is the recovery path for a lost authenticator
(F-29). No body. **204.**

| Status | `errorCode` | When |
|---|---|---|
| 400 | `User.2FANotEnabled` | 2FA is off |
| 404 | `User.NotFound` | |

#### `PUT /api/v1/admin/users/{id}/roles`

**Replaces** the whole role set. Service permission: `roles.manage` (plus `users.read`). Body
([`SetUserRolesRequest`](#setuserrolesrequest)): `roles`, at most 10 names, none blank.

- **Name matching.** Names match existing roles in any case and are trimmed; duplicates are dropped. Observed:
  `["user"," Admin ","ADMIN"]` gave `["User","Admin"]`.
- **Empty set.** **An omitted `roles`, `null` and `[]` all remove every role.**
- **Unknown names.** If any name is unknown, nothing changes.

**204.** Errors: 400 `Validation.Failed` (more than 10, or a blank name); 404 `Role.NotFound` (an unknown role); 404
`User.NotFound`.

The user's next token carries the new roles at once (the role cache is cleared). Tokens already issued keep the old
roles until they expire.

#### `POST /api/v1/admin/users/{id}/revoke-tokens`

Signs the user out everywhere: every refresh token is revoked. No body. **204.** Access tokens keep working until they
expire. Errors: 404 `User.NotFound`.

### Roles (`/api/v1/roles`)

**Auth, both layers:** gateway `Admin` role · service `Admin` **role** (not a permission). 401 and 403 have empty
bodies; for a customer, the gateway answers 403 itself.

Two different identifiers are used:

- **A role's id** (a GUID string) for `GET`, `PUT` and `DELETE /roles/{id}`. `/roles/Admin` treats `Admin` as an id and
  answers 404.
- **A role's name**, any case, for the membership endpoints `/roles/{roleName}/users…`.

Every write is recorded in the [audit trail](#audit-trail).

#### `GET /api/v1/roles`

All roles, sorted by name, as a **bare array** of [`Role`](#role):

```json
[{"id":"c12baa2c-f292-417f-b1da-69a5131be679","name":"Admin","description":"Admin role for the application"},
 {"id":"82dbd2dd-d48a-4c02-a8ea-f671ff07c725","name":"User","description":"User role for the application"}]
```

**Query:** `page` (default 1) and `pageSize` (default 50). There is no total and no maximum.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `ValidationError` | Shape (c): a non-integer `page` or `pageSize` (key `page`) |
| 500 | `InternalServerError` | `page=0`, or a negative `pageSize` (F-12) |

> ⚠ Only two roles exist by default. Request it without parameters and treat the array as the whole list. (F-12)

#### `POST /api/v1/roles`

Body ([`CreateRoleRequest`](#createrolerequest)): `name` (required, at most 256 characters, only letters, digits,
spaces, `_` and `-`) and `description` (optional, at most 250 characters).

**201** [`Role`](#role). `Location` names the internal host (F-32).

A new role grants **no permissions**: only `Admin` has a permission bundle
([conventions.md §5](conventions.md#5-permissions-and-admin-access)).

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | The name is missing or has a disallowed character, or the description is too long |
| 400 | `Role.Exists` | A role with this name exists, in any case |
| 400 | `Role.CreateFailed` | ASP.NET Identity refused it. From source, not observed |

#### `GET /api/v1/roles/{id}`

**200** [`Role`](#role). 404 `Role.NotFound`.

#### `PUT /api/v1/roles/{id}`

Changes the description; the name cannot be changed. Body ([`UpdateRoleRequest`](#updaterolerequest)):
`description`, at most 250 characters.

**204.** This **replaces** the description: an omitted or `null` `description` clears it (observed).

Errors: 400 `Validation.Failed`; 404 `Role.NotFound`; 400 `Role.UpdateFailed` (from source).

#### `DELETE /api/v1/roles/{id}`

**204.** The role is deleted even if users hold it; their membership goes with it.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Role.CannotDelete` | `Admin` or `User` |
| 404 | `Role.NotFound` | |
| 400 | `Role.DeleteFailed` | ASP.NET Identity refused it. From source, not observed |

> ⚠ For up to 5 minutes after the delete, a member who logs in still gets the deleted role **in the JWT**, although
> `user.roles` in the login response no longer lists it (observed). Today that has no effect, because such a role holds
> no permission. (F-33)

#### `GET /api/v1/roles/{roleName}/users`

Members of a role, sorted by email, as a **bare array** of [`UserInRole`](#userinrole). `page` and `pageSize` work as
in `GET /roles`, with no total. Here, out-of-range values do not fail (observed: `page=0&pageSize=-5` gave `[]`). 404
`Role.NotFound` for an unknown name.

#### `POST /api/v1/roles/{roleName}/users/{userId}`

Adds one role to a user. No body. **204.**

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Role.AddUserFailed` | The user already has the role (`detail` `"User already in role '…'."`) |
| 404 | `User.NotFound` | Unknown or deleted user |
| 404 | `Role.NotFound` | Unknown role |

#### `DELETE /api/v1/roles/{roleName}/users/{userId}`

Removes one role from a user. **204.**

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Role.RemoveUserFailed` | The user does not have the role |
| 404 | `User.NotFound`, `Role.NotFound` | As above |

Both membership calls clear the user's role cache, as `PUT /admin/users/{id}/roles` does. For the admin user screen,
prefer that endpoint: it sets the whole list in one call.

### Audit trail

Every Identity admin command (user writes, role writes, membership changes) writes one audit row, successful or not.
Rejected commands carry their `errorCode`. The admin panel reads the merged trail of all services from the gateway's
`GET /api/v1/admin/audit`, filtered with `service=identity`; see [admin-platform.md](admin-platform.md).

---

## Not callable by clients

| Endpoint | Why |
|---|---|
| `GET /api/v1/users/{userId}/contact` | Service to service: the Notification service reads a user's email with the `X-Internal-Api-Key` header. It is not routed (the gateway answers a bare 404). Called directly: no key → 401, a user JWT → 403, with the key → 200 `{ id, email, firstName, lastName }`, and an unknown or inactive user → 404 `Users.ContactNotFound` |
| `GET /api/v1/admin/audit` (on Identity) | Identity's own slice of the audit trail (`audit.read`). The gateway serves the merged trail at the same path, so this one is unreachable from outside |

---

## Types

C# sources: `Auth/Commands/*`, `Account/*`, `Users/*` and `Roles/*` in `EShop.Identity.Application`, plus the request
records in `EShop.Identity.API/Controllers`. All timestamps are UTC with `Z`, except `lockoutEnd` (see
[AdminUser](#adminuser)).

### Storefront types

#### RegisterRequest

| Field | Type | Notes |
|---|---|---|
| `email` | string | ≤ 256, an email address |
| `password` | string | 8–100; upper, lower, digit, other |
| `firstName` | string | 1–50; letters, spaces, `'`, `-` |
| `lastName` | string | as `firstName` |

#### RegisterResponse

| Field | Type | Notes |
|---|---|---|
| `userId` | string | The new user's id |
| `email` | string | As stored |
| `message` | string | Do not show (F-27) |

#### LoginRequest

| Field | Type | Notes |
|---|---|---|
| `email` | string | Required |
| `password` | string | Required |
| `twoFactorCode` | string, optional | Exactly 6 digits; only after `requires2FA: true` |

#### LoginResponse

A union of two shapes. Branch on `requires2FA`.

| Field | Signed in | 2FA required |
|---|---|---|
| `accessToken` | the JWT | `""` |
| `refreshToken` | opaque string | `""` |
| `expiresIn` | `3600` (seconds) | `0` |
| `tokenType` | `"Bearer"` | `"Bearer"` |
| `requires2FA` | `false` | `true` |
| `user` | [`UserSummary`](#usersummary) | `null` |

#### UserSummary

Source: `UserDto`. `id`, `email`, `firstName`, `lastName` (strings) and `roles` (string[]).

#### RefreshTokenRequest

`refreshToken` (string, required).

#### RevokeTokenRequest

`refreshToken` (string, required).

#### RefreshTokenResponse

`accessToken` (string), `refreshToken` (string, the new one), `expiresIn` (number, seconds).

#### ConfirmEmailRequest

`userId`, `token` (strings, required).

#### ForgotPasswordRequest

`email` (string, required).

#### ResetPasswordRequest

`userId`, `token` (decoded), `newPassword` (strings, required).

#### SuccessMessageResponse

Returned by confirm-email, forgot-password, reset-password and disable-2fa. `success` (always `true` on a 2xx) and
`message` (string, safe to show).

#### MessageResponse

Returned by `PUT profile` and `change-password`. `message` (string).

#### UpdateProfileRequest

| Field | Type | Notes |
|---|---|---|
| `firstName` | string | Required on every call |
| `lastName` | string | Required on every call |
| `profilePictureUrl` | string \| null, optional | Omitted or `null`: keep. `""`: remove. URL: replace |

#### ChangePasswordRequest

`currentPassword` (string), `newPassword` (string, 8–100, four classes, different from the current one).

#### UserProfile

Source: `UserProfileResponse`.

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `id` | string | no | |
| `email` | string | no | |
| `firstName`, `lastName` | string | no | |
| `profilePictureUrl` | string | yes | |
| `emailConfirmed` | boolean | no | |
| `twoFactorEnabled` | boolean | no | |
| `isActive` | boolean | no | Always `true` here: an inactive account gets an error instead |
| `createdAt` | string (date-time) | no | |
| `lastLoginAt` | string (date-time) | yes | Can lag by up to 5 minutes (cache) |
| `roles` | string[] | no | |

#### Enable2FAResponse

`sharedKey` (string, lowercase groups of four), `qrCodeUri` (string, `otpauth://totp/…`), `message` (string).

#### TwoFactorCodeRequest

The body of verify-2fa and disable-2fa. `code` (string, exactly 6 digits).

#### Verify2FAResponse

`success` (`true`), `recoveryCodes` (string[], ten codes such as `"R84W6-KX6BP"`, unusable today, F-29), `message`
(string).

### Admin types

#### AdminUserSortBy

A request-only enum: the `sortBy` query value. Send the name. The server also accepts it in any case, or as the
integers `0`, `1`, `2`.

| Name | Integer | Sorts by |
|---|---|---|
| `CreatedAt` | 0 | Creation time (default) |
| `Email` | 1 | Email |
| `LastLoginAt` | 2 | Last login; never-logged-in users sort as `null` |

#### AdminUserListQuery

See the parameter table of [`GET /admin/users`](#get-apiv1adminusers).

#### AdminUser

A list item. Source: `AdminUserDto`.

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `id` | string | no | |
| `email` | string | yes | Always set for accounts created through the API |
| `userName` | string | yes | Equal to `email` |
| `firstName`, `lastName` | string | no | |
| `emailConfirmed` | boolean | no | |
| `twoFactorEnabled` | boolean | no | |
| `isActive` | boolean | no | `false` after deactivate, delete and restore |
| `isDeleted` | boolean | no | |
| `deletedAt` | string (date-time) | yes | Set while deleted |
| `createdAt` | string (date-time) | no | |
| `lastLoginAt` | string (date-time) | yes | `null` until the first login |
| `lockoutEnd` | string (date-time) | yes | **`+00:00` form, whole seconds** (`"2026-09-23T18:59:25+00:00"`). Set only by the admin lock. Parse with `new Date()` (F-35) |
| `isLockedOut` | boolean | no | `lockoutEnd` is in the future. It does **not** reflect the failed-login block |
| `roles` | string[] | no | Kept while the user is deleted |

#### AdminUserDetails

Everything in [`AdminUser`](#adminuser), plus:

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `phoneNumber` | string | yes | |
| `profilePictureUrl` | string | yes | |
| `phoneNumberConfirmed` | boolean | no | Nothing sets it today |
| `lastLoginIp` | string | yes | The client IP of the last login |
| `lockoutEnabled` | boolean | no | `true` for every account |
| `accessFailedCount` | number | no | Always `0` today: the failed-login counter is kept elsewhere |
| `hasGoogleLogin`, `hasGitHubLogin` | boolean | no | Always `false` today: there are no external logins |

#### AdminUserStats

`total`, `newInPeriod`, `active`, `locked`, `unconfirmed`, `deleted`: all numbers. See
[`GET /admin/users/stats`](#get-apiv1adminusersstats).

#### AdminUserSession

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `id` | string (GUID) | no | |
| `createdAt` | string (date-time) | no | Sign-in or refresh time |
| `expiresAt` | string (date-time) | no | 7 days after `createdAt` |
| `createdByIp` | string | yes | |
| `revokedAt` | string (date-time) | yes | |
| `revokedByIp` | string | yes | |
| `revokeReason` | string | yes | See [sessions](#get-apiv1adminusersidsessions) |
| `isActive` | boolean | no | Neither revoked nor expired |

#### CreateUserRequest

`email`, `firstName`, `lastName` (strings, required); `phoneNumber`, `password` (string | null, optional); `roles`
(string[] | null, optional); `emailConfirmed` (boolean, optional). Rules in [`POST /admin/users`](#post-apiv1adminusers).

#### CreateUserResponse

`userId` (string), `email` (string) and `inviteSent` (boolean).

#### UpdateUserRequest

`firstName`, `lastName`, `phoneNumber`, `profilePictureUrl`: all optional, `string | null`. See
[`PUT /admin/users/{id}`](#put-apiv1adminusersid).

#### ChangeUserEmailRequest

`email` (string, required) and `markConfirmed` (boolean, optional, default `false`).

#### LockUserRequest

`until` (date-time string, required, in the future) and `reason` (string, optional, ≤ 250).

#### SetUserRolesRequest

`roles` (string[] or `null`, optional). The complete new set.

#### Role

`id` (string), `name` (string), `description` (string | null).

#### CreateRoleRequest

`name` (string, required) and `description` (string | null, optional).

#### UpdateRoleRequest

`description` (string | null). It replaces the stored description; omitted or `null` clears it.

#### UserInRole

`id`, `email`, `firstName`, `lastName` (strings).

### TypeScript

`PagedResult<T>` and `ProblemDetails` are in [conventions.md](conventions.md#6-paging).

```ts
// ---- Storefront: requests ----

export interface RegisterRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
}

export interface LoginRequest {
  email: string;
  password: string;
  /** Send only on the second attempt, after `requires2FA: true`. Six digits. */
  twoFactorCode?: string;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

export interface RevokeTokenRequest {
  refreshToken: string;
}

export interface ConfirmEmailRequest {
  userId: string;
  token: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  userId: string;
  token: string;
  newPassword: string;
}

export interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
  /** Omit (or null) to keep the stored value, "" to remove it, or an absolute http(s) URL. */
  profilePictureUrl?: string | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

/** Body of verify-2fa and disable-2fa. */
export interface TwoFactorCodeRequest {
  code: string;
}

// ---- Storefront: responses ----

export interface RegisterResponse {
  userId: string;
  email: string;
  /** Do not show it: it promises an email that is never sent (F-27). */
  message: string;
}

export interface UserSummary {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
}

export interface LoginSuccess {
  accessToken: string;
  refreshToken: string;
  /** Seconds; 3600. */
  expiresIn: number;
  tokenType: 'Bearer';
  requires2FA: false;
  user: UserSummary;
}

/** Password accepted, second factor still needed: resend the same login with `twoFactorCode`. */
export interface LoginTwoFactorRequired {
  accessToken: '';
  refreshToken: '';
  expiresIn: 0;
  tokenType: 'Bearer';
  requires2FA: true;
  user: null;
}

export type LoginResponse = LoginSuccess | LoginTwoFactorRequired;

export interface RefreshTokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

/** confirm-email, forgot-password, reset-password, disable-2fa. `success` is always true on a 2xx. */
export interface SuccessMessageResponse {
  success: true;
  message: string;
}

/** PUT profile, change-password. */
export interface MessageResponse {
  message: string;
}

export interface UserProfile {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  profilePictureUrl: string | null;
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  isActive: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  roles: string[];
}

export interface Enable2FAResponse {
  /** The secret in lowercase groups of four, for manual entry. */
  sharedKey: string;
  /** An otpauth:// URI. Render it as a QR code on the client. */
  qrCodeUri: string;
  message: string;
}

export interface Verify2FAResponse {
  success: true;
  /** Ten codes such as "R84W6-KX6BP". No endpoint accepts them yet (F-29). */
  recoveryCodes: string[];
  message: string;
}
```

```ts
// ---- Admin: users ----

/** Sort key for GET /api/v1/admin/users. Send the name. */
export type AdminUserSortBy = 'CreatedAt' | 'Email' | 'LastLoginAt';

export interface AdminUserListQuery {
  search?: string;
  role?: string;
  isActive?: boolean;
  /** true lists ONLY deleted users; false or omitted lists only live ones. */
  isDeleted?: boolean;
  emailConfirmed?: boolean;
  twoFactorEnabled?: boolean;
  /** ISO-8601 with Z or an offset. A bare date answers 500 (F-15). */
  createdFrom?: string;
  createdTo?: string;
  lastLoginFrom?: string;
  lastLoginTo?: string;
  sortBy?: AdminUserSortBy;
  isDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export interface AdminUserStatsQuery {
  from?: string;
  to?: string;
}

export interface AdminUser {
  id: string;
  email: string | null;
  userName: string | null;
  firstName: string;
  lastName: string;
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  isActive: boolean;
  isDeleted: boolean;
  deletedAt: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  /** DateTimeOffset: "2026-09-23T18:59:25+00:00", not Z. */
  lockoutEnd: string | null;
  isLockedOut: boolean;
  roles: string[];
}

export interface AdminUserDetails extends AdminUser {
  phoneNumber: string | null;
  profilePictureUrl: string | null;
  phoneNumberConfirmed: boolean;
  lastLoginIp: string | null;
  lockoutEnabled: boolean;
  accessFailedCount: number;
  hasGoogleLogin: boolean;
  hasGitHubLogin: boolean;
}

export interface AdminUserStats {
  total: number;
  newInPeriod: number;
  active: number;
  locked: number;
  unconfirmed: number;
  deleted: number;
}

export interface AdminUserSession {
  id: string;
  createdAt: string;
  expiresAt: string;
  createdByIp: string | null;
  revokedAt: string | null;
  revokedByIp: string | null;
  revokeReason: string | null;
  isActive: boolean;
}

export interface CreateUserRequest {
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string | null;
  /** Omit to create the account without a password and email a set-password link. */
  password?: string | null;
  /** Omit for ["User"]; [] creates a user with no role. At most 10. */
  roles?: string[] | null;
  emailConfirmed?: boolean;
}

export interface CreateUserResponse {
  userId: string;
  email: string;
  /** true when no password was given and a set-password email was queued. */
  inviteSent: boolean;
}

/** Every field optional: omitted or null leaves it unchanged; "" clears phoneNumber / profilePictureUrl. */
export interface UpdateUserRequest {
  firstName?: string | null;
  lastName?: string | null;
  phoneNumber?: string | null;
  profilePictureUrl?: string | null;
}

export interface ChangeUserEmailRequest {
  email: string;
  /** Omitted means false: the new address becomes unconfirmed. */
  markConfirmed?: boolean;
}

export interface LockUserRequest {
  /** Required, in the future. ISO-8601 with Z or an offset. */
  until: string;
  reason?: string | null;
}

export interface SetUserRolesRequest {
  /** The complete new set. Omitted or [] removes every role. */
  roles?: string[] | null;
}

// ---- Admin: roles ----

export interface Role {
  id: string;
  name: string;
  description: string | null;
}

export interface CreateRoleRequest {
  name: string;
  description?: string | null;
}

export interface UpdateRoleRequest {
  /** Replaces the description; omitted or null clears it. */
  description?: string | null;
}

export interface UserInRole {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
}
```

---

## Frontend notes

> ⚠ **Failed-login block.** Three failed logins block an email for 15 minutes after the last failure, even with the
> right password, while `detail` says to wait 2 seconds. Show "try again later or reset your password", and offer an
> admin `unlock`. (F-28)

> ⚠ **No confirmation email.** Registration's `message` promises one, but none is sent, and `confirm-email` cannot be
> completed by a user. (F-27, F-30)

> ⚠ **Recovery codes are not accepted anywhere.** A lost authenticator needs an admin `disable-2fa`. (F-29)

> ⚠ **Change password signs this client out.** After a 200, log in again with the new password. (Behaviour by design.)

> ⚠ **Account-state errors are inconsistent.** A deleted or deactivated account is reported with different codes and
> statuses depending on the endpoint (`Account.NotFound`, `Account.UserNotFound`, `Auth.AccountDisabled`; 404, 400 or
> 401). Treat all of them as "sign out". (F-34)

> ⚠ **Dates in admin filters need `Z`.** A bare date on `/admin/users` or `/admin/users/stats` answers 500. Send
> `toISOString()`. (F-15)

> ⚠ **`lockoutEnd` ends in `+00:00`, not `Z`.** Parse it with `new Date()`, never by string comparison. (F-35)

> ⚠ **Roles lists are bare arrays**, with `page`/`pageSize` but no total, and `page=0` answers 500. Call `GET /roles`
> without parameters. (F-12)

> ⚠ **`Location` names an internal host.** On a 201, read the new id from the body. (F-32, F-04)

> ⚠ **The OpenAPI document is misleading here.** Its paths are PascalCase (F-19), it lists four ignored `Effective*`
> parameters (F-31), and it declares no 401 on `/account/*` and no 400 or 500 on `GET /roles` (F-11).

> ⚠ **Error bodies have no `type`/`title`** (F-21), and three validation shapes occur (F-03). Branch on `errorCode`.

---

## Related documents

- [conventions.md](conventions.md): errors, auth, rate limits, paging
- [admin-platform.md](admin-platform.md): the merged audit trail
- [flows.md](flows.md): sign-up, login with 2FA, refresh and logout as sequences

---

**Version**: 1.0  
**Last Updated**: 2026-09-23
