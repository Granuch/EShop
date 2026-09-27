# Endpoint index

Every endpoint of the platform on one page: method, path, auth at the gateway and in the service, audience, and a link to its contract. Cross-checked against every service's live `/openapi/v1.json`: every operation each document lists has a route here, and every route here is documented.

**Verified at:** `85b69f5` on `feature/admin-panel` (2026-09-25); the gateway auth column at `3217d43`, which fixed F-08, F-44 and F-45. 141 application endpoints across six services plus 4 the gateway serves itself.

Legend: **Aud** = audience (Storefront / Admin panel / Internal, not callable by a client). "anon" = no token needed. `p:x.y` = permission policy. `R:Admin` = the `Admin` role/policy. `AdminArea` = the gateway's admin gate: the caller holds at least one permission ([conventions.md §5](conventions.md#5-permissions-and-admin-access)).

---

## Identity (42)

| Method | Path | Gateway auth | Service auth | Aud | Docs |
|---|---|---|---|---|---|
| POST | `/api/v1/auth/register` | anon | anon, rl `auth` | Storefront | [link](identity.md#post-apiv1authregister) |
| POST | `/api/v1/auth/login` | anon | anon, rl `login` | Storefront | [link](identity.md#post-apiv1authlogin) |
| POST | `/api/v1/auth/refresh-token` | anon | anon, rl `auth` | Storefront | [link](identity.md#post-apiv1authrefresh-token) |
| POST | `/api/v1/auth/revoke-token` | anon | anon, rl `auth` | Storefront | [link](identity.md#post-apiv1authrevoke-token) |
| POST | `/api/v1/auth/confirm-email` | anon | anon, rl `auth` | Storefront | [link](identity.md#post-apiv1authconfirm-email) |
| POST | `/api/v1/auth/forgot-password` | anon | anon, rl `login` | Storefront | [link](identity.md#post-apiv1authforgot-password) |
| POST | `/api/v1/auth/reset-password` | anon | anon, rl `login` | Storefront | [link](identity.md#post-apiv1authreset-password) |
| GET | `/api/v1/account/profile` | Authenticated | auth | Storefront | [link](identity.md#get-apiv1accountprofile) |
| PUT | `/api/v1/account/profile` | Authenticated | auth | Storefront | [link](identity.md#put-apiv1accountprofile) |
| POST | `/api/v1/account/change-password` | Authenticated | auth | Storefront | [link](identity.md#post-apiv1accountchange-password) |
| POST | `/api/v1/account/enable-2fa` | Authenticated | auth | Storefront | [link](identity.md#post-apiv1accountenable-2fa) |
| POST | `/api/v1/account/verify-2fa` | Authenticated | auth | Storefront | [link](identity.md#post-apiv1accountverify-2fa) |
| POST | `/api/v1/account/disable-2fa` | Authenticated | auth | Storefront | [link](identity.md#post-apiv1accountdisable-2fa) |
| GET | `/api/v1/admin/users` | AdminArea | p:users.read | Admin | [link](identity.md#get-apiv1adminusers) |
| GET | `/api/v1/admin/users/stats` | AdminArea | p:users.read | Admin | [link](identity.md#get-apiv1adminusersstats) |
| GET | `/api/v1/admin/users/{id}` | AdminArea | p:users.read | Admin | [link](identity.md#get-apiv1adminusersid) |
| GET | `/api/v1/admin/users/{id}/roles` | AdminArea | p:users.read | Admin | [link](identity.md#get-apiv1adminusersidroles) |
| GET | `/api/v1/admin/users/{id}/sessions` | AdminArea | p:users.read | Admin | [link](identity.md#get-apiv1adminusersidsessions) |
| POST | `/api/v1/admin/users` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusers) |
| PUT | `/api/v1/admin/users/{id}` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#put-apiv1adminusersid) |
| PUT | `/api/v1/admin/users/{id}/email` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#put-apiv1adminusersidemail) |
| POST | `/api/v1/admin/users/{id}/activate` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersidactivate) |
| POST | `/api/v1/admin/users/{id}/deactivate` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersiddeactivate) |
| DELETE | `/api/v1/admin/users/{id}` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#delete-apiv1adminusersid) |
| POST | `/api/v1/admin/users/{id}/restore` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersidrestore) |
| POST | `/api/v1/admin/users/{id}/lock` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersidlock) |
| POST | `/api/v1/admin/users/{id}/unlock` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersidunlock) |
| POST | `/api/v1/admin/users/{id}/reset-password` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersidreset-password) |
| POST | `/api/v1/admin/users/{id}/confirm-email` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersidconfirm-email) |
| POST | `/api/v1/admin/users/{id}/disable-2fa` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersiddisable-2fa) |
| PUT | `/api/v1/admin/users/{id}/roles` | AdminArea | p:users.read + p:roles.manage | Admin | [link](identity.md#put-apiv1adminusersidroles) |
| POST | `/api/v1/admin/users/{id}/revoke-tokens` | AdminArea | p:users.read + p:users.manage | Admin | [link](identity.md#post-apiv1adminusersidrevoke-tokens) |
| GET | `/api/v1/roles` | AdminArea | R:Admin | Admin | [link](identity.md#get-apiv1roles) |
| POST | `/api/v1/roles` | AdminArea | R:Admin | Admin | [link](identity.md#post-apiv1roles) |
| GET | `/api/v1/roles/{id}` | AdminArea | R:Admin | Admin | [link](identity.md#get-apiv1rolesid) |
| PUT | `/api/v1/roles/{id}` | AdminArea | R:Admin | Admin | [link](identity.md#put-apiv1rolesid) |
| DELETE | `/api/v1/roles/{id}` | AdminArea | R:Admin | Admin | [link](identity.md#delete-apiv1rolesid) |
| GET | `/api/v1/roles/{roleName}/users` | AdminArea | R:Admin | Admin | [link](identity.md#get-apiv1rolesrolenameusers) |
| POST | `/api/v1/roles/{roleName}/users/{userId}` | AdminArea | R:Admin | Admin | [link](identity.md#post-apiv1rolesrolenameusersuserid) |
| DELETE | `/api/v1/roles/{roleName}/users/{userId}` | AdminArea | R:Admin | Admin | [link](identity.md#delete-apiv1rolesrolenameusersuserid) |
| GET | `/api/v1/users/{userId}/contact` | — | InternalService API key | Internal | [link](identity.md#not-callable-by-clients) |
| GET | `/api/v1/admin/audit` | — (gateway serves its own) | p:audit.read | Internal | [link](identity.md#not-callable-by-clients) |

## Catalog (42)

| Method | Path | Gateway auth | Service auth | Aud | Docs |
|---|---|---|---|---|---|
| GET | `/api/v1/products` | anon | anon (admin sees drafts), rl `search` | Storefront | [link](catalog.md#get-apiv1products) |
| GET | `/api/v1/products/newest` | anon | anon (admin sees drafts), rl `search` | Storefront | [link](catalog.md#get-apiv1productsnewest) |
| GET | `/api/v1/products/{id}` | anon | anon (admin sees drafts) | Storefront | [link](catalog.md#get-apiv1productsid) |
| GET | `/api/v1/products/deleted` | AdminArea | R:Admin | Admin | [link](catalog.md#get-apiv1productsdeleted) |
| POST | `/api/v1/products` | AdminArea | R:Admin | Admin | [link](catalog.md#post-apiv1products) |
| PUT | `/api/v1/products/{id}` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1productsid) |
| DELETE | `/api/v1/products/{id}` | AdminArea | R:Admin | Admin | [link](catalog.md#delete-apiv1productsid) |
| POST | `/api/v1/products/{id}/restore` | AdminArea | R:Admin | Admin | [link](catalog.md#post-apiv1productsidrestore) |
| PATCH | `/api/v1/products/{id}/stock` | AdminArea | R:Admin | Admin | [link](catalog.md#patch-apiv1productsidstock) |
| POST | `/api/v1/products/{id}/publish` | AdminArea | R:Admin | Admin | [link](catalog.md#post-apiv1productsidpublish) |
| POST | `/api/v1/products/{id}/unpublish` | AdminArea | R:Admin | Admin | [link](catalog.md#post-apiv1productsidunpublish) |
| PUT | `/api/v1/products/{id}/discount` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1productsiddiscount) |
| DELETE | `/api/v1/products/{id}/discount` | AdminArea | R:Admin | Admin | [link](catalog.md#delete-apiv1productsiddiscount) |
| POST | `/api/v1/products/{id}/images` | AdminArea | R:Admin | Admin | [link](catalog.md#post-apiv1productsidimages) |
| PUT | `/api/v1/products/{id}/images/reorder` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1productsidimagesreorder) |
| PUT | `/api/v1/products/{id}/images/{imageId}` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1productsidimagesimageid) |
| DELETE | `/api/v1/products/{id}/images/{imageId}` | AdminArea | R:Admin | Admin | [link](catalog.md#delete-apiv1productsidimagesimageid) |
| PUT | `/api/v1/products/{id}/images/{imageId}/main` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1productsidimagesimageidmain) |
| POST | `/api/v1/products/{id}/attributes` | AdminArea | R:Admin | Admin | [link](catalog.md#post-apiv1productsidattributes) |
| PUT | `/api/v1/products/{id}/attributes` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1productsidattributes) |
| PUT | `/api/v1/products/{id}/attributes/{attributeId}` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1productsidattributesattributeid) |
| DELETE | `/api/v1/products/{id}/attributes/{attributeId}` | AdminArea | R:Admin | Admin | [link](catalog.md#delete-apiv1productsidattributesattributeid) |
| GET | `/api/v1/categories` | anon | anon (admin sees inactive) | Storefront | [link](catalog.md#get-apiv1categories) |
| GET | `/api/v1/categories/{id}` | anon | anon | Storefront | [link](catalog.md#get-apiv1categoriesid) |
| GET | `/api/v1/categories/{id}/products` | anon | anon (admin sees drafts) | Storefront | [link](catalog.md#get-apiv1categoriesidproducts) |
| POST | `/api/v1/categories` | AdminArea | R:Admin | Admin | [link](catalog.md#post-apiv1categories) |
| PUT | `/api/v1/categories/{id}` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1categoriesid) |
| DELETE | `/api/v1/categories/{id}` | AdminArea | R:Admin | Admin | [link](catalog.md#delete-apiv1categoriesid) |
| PUT | `/api/v1/categories/{id}/parent` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1categoriesidparent) |
| POST | `/api/v1/categories/{id}/restore` | AdminArea | R:Admin | Admin | [link](catalog.md#post-apiv1categoriesidrestore) |
| PUT | `/api/v1/categories/reorder` | AdminArea | R:Admin | Admin | [link](catalog.md#put-apiv1categoriesreorder) |
| GET | `/api/v1/categories/{id}/stats` | AdminArea | R:Admin | Admin | [link](catalog.md#category-statistics) |
| POST | `/api/v1/products/bulk/publish` | AdminArea | R:Admin, rl `bulk` | Admin | [link](catalog.md#bulk-product-actions) |
| POST | `/api/v1/products/bulk/unpublish` | AdminArea | R:Admin, rl `bulk` | Admin | [link](catalog.md#bulk-product-actions) |
| POST | `/api/v1/products/bulk/delete` | AdminArea | R:Admin, rl `bulk` | Admin | [link](catalog.md#bulk-product-actions) |
| POST | `/api/v1/products/bulk/category` | AdminArea | R:Admin, rl `bulk` | Admin | [link](catalog.md#bulk-product-actions) |
| POST | `/api/v1/products/bulk/price` | AdminArea | R:Admin, rl `bulk` | Admin | [link](catalog.md#bulk-product-actions) |
| POST | `/api/v1/products/import` | AdminArea | R:Admin, rl `bulk` | Admin | [link](catalog.md#product-import) |
| GET | `/api/v1/products/export` | AdminArea | R:Admin, rl `bulk` | Admin | [link](catalog.md#product-export) |
| GET | `/api/v1/admin/catalog/low-stock` | AdminArea | R:Admin | Admin | [link](catalog.md#low-stock) |
| POST | `/api/v1/admin/cache/invalidate` | AdminArea | p:system.manage | Admin | [link](catalog.md#cache) |
| GET | `/api/v1/admin/audit` | — | p:audit.read | Internal | [link](catalog.md#not-callable-by-clients) |

## Basket (11)

| Method | Path | Gateway auth | Service auth | Aud | Docs |
|---|---|---|---|---|---|
| GET | `/api/v1/basket/{userId}` | Authenticated | owner or admin (read) | Storefront | [link](basket.md#get-apiv1basketuserid) |
| DELETE | `/api/v1/basket/{userId}` | Authenticated | owner | Storefront | [link](basket.md#delete-apiv1basketuserid) |
| POST | `/api/v1/basket/{userId}/items` | Authenticated | owner | Storefront | [link](basket.md#post-apiv1basketuseriditems) |
| PUT | `/api/v1/basket/{userId}/items/{productId}` | Authenticated | owner | Storefront | [link](basket.md#put-apiv1basketuseriditemsproductid) |
| DELETE | `/api/v1/basket/{userId}/items/{productId}` | Authenticated | owner | Storefront | [link](basket.md#delete-apiv1basketuseriditemsproductid) |
| POST | `/api/v1/basket/{userId}/checkout` | Authenticated | owner | Storefront | [link](basket.md#post-apiv1basketuseridcheckout) |
| GET | `/api/v1/basket/admin/carts` | AdminArea | p:baskets.read | Admin | [link](basket.md#get-apiv1basketadmincarts) |
| GET | `/api/v1/basket/admin/abandoned` | AdminArea | p:baskets.read | Admin | [link](basket.md#get-apiv1basketadminabandoned) |
| GET | `/api/v1/basket/admin/outbox/dead-letters/details` | AdminArea | p:system.manage | Admin | [link](basket.md#get-apiv1basketadminoutboxdead-lettersdetails) |
| GET | `/api/v1/basket/admin/outbox/dead-letters` | AdminArea | R:Admin | Admin | [link](basket.md#get-apiv1basketadminoutboxdead-letters) |
| POST | `/api/v1/basket/admin/outbox/dead-letters/replay` | AdminArea | R:Admin | Admin | [link](basket.md#post-apiv1basketadminoutboxdead-lettersreplay) |

## Ordering (17)

| Method | Path | Gateway auth | Service auth | Aud | Docs |
|---|---|---|---|---|---|
| POST | `/api/v1/orders` | Authenticated | auth (userId forced for non-admin) | Storefront | [link](ordering.md#post-apiv1orders) |
| GET | `/api/v1/orders/{id}` | Authenticated | OrderOwnerOrAdmin | Storefront | [link](ordering.md#get-apiv1ordersid) |
| GET | `/api/v1/users/{userId}/orders` | Authenticated (GET only) | SameUserOrAdmin | Storefront | [link](ordering.md#get-apiv1usersuseridorders) |
| POST | `/api/v1/orders/{id}/items` | Authenticated | OrderOwnerOrAdmin | Storefront | [link](ordering.md#post-apiv1ordersiditems) |
| PUT | `/api/v1/orders/{id}/items/{itemId}` | Authenticated | OrderOwnerOrAdmin | Storefront | [link](ordering.md#put-apiv1ordersiditemsitemid) |
| DELETE | `/api/v1/orders/{id}/items/{itemId}` | Authenticated | OrderOwnerOrAdmin | Storefront | [link](ordering.md#delete-apiv1ordersiditemsitemid) |
| PUT | `/api/v1/orders/{id}/shipping-address` | Authenticated | OrderOwnerOrAdmin | Storefront | [link](ordering.md#put-apiv1ordersidshipping-address) |
| POST | `/api/v1/orders/{id}/cancel` | Authenticated | OrderOwnerOrAdmin | Storefront | [link](ordering.md#post-apiv1ordersidcancel) |
| GET | `/api/v1/orders` | AdminArea | R:Admin | Admin | [link](ordering.md#get-apiv1orders) |
| GET | `/api/v1/orders/stats` | AdminArea | R:Admin | Admin | [link](ordering.md#get-apiv1ordersstats) |
| POST | `/api/v1/orders/{id}/notes` | AdminArea | R:Admin | Admin | [link](ordering.md#post-apiv1ordersidnotes) |
| GET | `/api/v1/orders/{id}/notes` | AdminArea | R:Admin | Admin | [link](ordering.md#get-apiv1ordersidnotes) |
| GET | `/api/v1/orders/{id}/history` | AdminArea | R:Admin | Admin | [link](ordering.md#get-apiv1ordersidhistory) |
| POST | `/api/v1/orders/{id}/ship` | AdminArea | R:Admin | Admin | [link](ordering.md#post-apiv1ordersidship) |
| POST | `/api/v1/orders/{id}/deliver` | AdminArea | R:Admin | Admin | [link](ordering.md#post-apiv1ordersiddeliver) |
| GET | `/api/v1/admin/settings` | — (gateway serves its own) | p:system.manage | Internal | [link](ordering.md#not-callable-by-clients) |
| GET | `/api/v1/admin/audit` | — | p:audit.read | Internal | [link](ordering.md#not-callable-by-clients) |

## Payment (16)

| Method | Path | Gateway auth | Service auth | Aud | Docs |
|---|---|---|---|---|---|
| POST | `/api/v1/payments/create-intent` | Authenticated | auth | Storefront | [link](payment.md#post-apiv1paymentscreate-intent) |
| GET | `/api/v1/payments/{id}` | Authenticated | auth (other user's → 404) | Storefront | [link](payment.md#get-apiv1paymentsid) |
| GET | `/api/v1/users/{userId}/payments` | Authenticated | SameUserOrAdmin | Storefront | [link](payment.md#get-apiv1usersuseridpayments) |
| GET | `/api/v1/payments` | AdminArea | p:payments.read | Admin | [link](payment.md#get-apiv1payments) |
| GET | `/api/v1/payments/stats` | AdminArea | p:payments.read | Admin | [link](payment.md#get-apiv1paymentsstats) |
| GET | `/api/v1/payments/export` | AdminArea | p:payments.read | Admin | [link](payment.md#get-apiv1paymentsexport) |
| GET | `/api/v1/payments/{id}/events` | AdminArea | p:payments.read | Admin | [link](payment.md#get-apiv1paymentsidevents) |
| POST | `/api/v1/payments/offline` | AdminArea | p:payments.write | Admin | [link](payment.md#post-apiv1paymentsoffline) |
| POST | `/api/v1/payments/webhooks/failed/replay` | AdminArea | p:payments.write | Admin | [link](payment.md#post-apiv1paymentswebhooksfailedreplay) |
| POST | `/api/v1/payments` | AdminArea | R:Admin | Admin | [link](payment.md#post-apiv1payments) |
| POST | `/api/v1/payments/{id}/refund` | AdminArea | R:Admin | Admin | [link](payment.md#post-apiv1paymentsidrefund) |
| GET | `/api/v1/payments/simulation` | AdminArea | R:Admin | Admin | [link](payment.md#get-apiv1paymentssimulation) |
| POST | `/webhooks/stripe` | — | anon + Stripe-Signature, no rate limit | Internal | [link](payment.md#not-callable-by-clients) |
| GET | `/api/v1/admin/settings` | — | p:system.manage | Internal | [link](payment.md#not-callable-by-clients) |
| GET | `/api/v1/admin/feature-flags` | — | p:system.manage | Internal | [link](payment.md#not-callable-by-clients) |
| GET | `/api/v1/admin/audit` | — | p:audit.read | Internal | [link](payment.md#not-callable-by-clients) |

## Notification (9)

| Method | Path | Gateway auth | Service auth | Aud | Docs |
|---|---|---|---|---|---|
| GET | `/api/v1/notifications` | AdminArea | p:notifications.read | Admin | [link](notification.md#get-apiv1notifications) |
| GET | `/api/v1/notifications/stats` | AdminArea | p:notifications.read | Admin | [link](notification.md#get-apiv1notificationsstats) |
| GET | `/api/v1/notifications/{id}` | AdminArea | p:notifications.read | Admin | [link](notification.md#get-apiv1notificationsid) |
| GET | `/api/v1/notifications/templates` | AdminArea | p:notifications.read | Admin | [link](notification.md#get-apiv1notificationstemplates) |
| POST | `/api/v1/notifications/templates/{name}/test` | AdminArea | p:notifications.manage | Admin | [link](notification.md#post-apiv1notificationstemplatesnametest) |
| POST | `/api/v1/notifications/retry-failed` | AdminArea | p:notifications.manage | Admin | [link](notification.md#post-apiv1notificationsretry-failed) |
| POST | `/api/v1/notifications/{id}/resend` | AdminArea | p:notifications.manage | Admin | [link](notification.md#post-apiv1notificationsidresend) |
| POST | `/api/v1/notifications/{id}/mark-undeliverable` | AdminArea | p:notifications.manage | Admin | [link](notification.md#post-apiv1notificationsidmark-undeliverable) |
| GET | `/api/v1/admin/audit` | — | p:audit.read | Internal | [link](notification.md#not-callable-by-clients) |

## Gateway-served (4)

| Method | Path | Gateway auth | Service auth | Aud | Docs |
|---|---|---|---|---|---|
| GET | `/api/v1/admin/audit` | p:audit.read (merges Identity, Catalog, Ordering, Payment, Notification) | each service checks p:audit.read again on its own slice | Admin | [link](admin-platform.md#get-apiv1adminaudit) |
| GET | `/api/v1/admin/health` | p:system.manage | none: each service's /health is anonymous | Admin | [link](admin-platform.md#get-apiv1adminhealth) |
| GET | `/api/v1/admin/settings` | p:system.manage (Ordering + Payment) | Ordering and Payment check p:system.manage again | Admin | [link](admin-platform.md#get-apiv1adminsettings) |
| GET | `/api/v1/admin/feature-flags` | p:system.manage (Payment + gateway simulation) | Payment checks p:system.manage again | Admin | [link](admin-platform.md#get-apiv1adminfeature-flags) |

Total: **141** application endpoints (Identity 42 · Catalog 42 · Basket 11 · Ordering 17 · Payment 16 · Notification 9 · Gateway 4).

---

## Infrastructure (documented once, not per endpoint)

Every service and the gateway also serve `GET /` (anonymous info object), `GET /health`, `/health/ready`, `/health/live` (anonymous, not routed through the gateway), `GET /prometheus` and `/metrics` (loopback/private networks only), and `GET /openapi/v1.json` / `/scalar/v1` (not in Production; Payment and the gateway have no Scalar). See [conventions.md §12](conventions.md#12-infrastructure-endpoints).

---

**Version**: 1.0  
**Last Updated**: 2026-09-25
