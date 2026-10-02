#!/usr/bin/env python3
"""End-to-end smoke test of a running EShop compose stack, through the API gateway.

Walks one customer from sign-up to a delivered order:
register -> login (unconfirmed: allowed, token says email_verified=false) -> browse -> basket
-> checkout refused (403 Auth.EmailNotConfirmed) -> confirmation email (Mailpit) -> confirm -> refresh (token says true)
-> checkout -> order appears -> admin settles the payment offline -> order Paid -> ship -> deliver,
then checks the customer received the expected emails. On the way it also checks that a protected route refuses an
anonymous caller (ported from the old scripts/gateway-verify-target.ps1) and, when SMOKE_CHECK_TRACES is set, that
every service's spans reached Jaeger under its own name during the run (the docker-ci DC-35 regression).

Before sending anything it refuses to run if Notification is not pointed at Mailpit: a local .env may configure a
real SMTP server, and the run registers a customer and ships an order, i.e. sends real email (docker-ci DC-49).

Standard library only, so it runs unchanged on a developer machine and on a GitHub runner.
Exit code 0 on success, 1 on the first failed step (with the step name and the response).

Environment (all optional):
  GATEWAY_URL     default http://localhost:7000
  MAILPIT_URL     default http://localhost:8025
  ADMIN_EMAIL     default admin@eshop.com      (compose's IDENTITY_SEED_ADMIN_EMAIL default)
  ADMIN_PASSWORD  default Admin123!            (compose's IDENTITY_SEED_ADMIN_PASSWORD default)
  SMOKE_TIMEOUT   seconds to wait for each asynchronous step, default 90
  SMOKE_CHECK_TRACES  set to 1 to require spans from all seven services in Jaeger (needs the monitoring profile and
                      a sampling ratio of 1.0, as the Docker Smoke workflow sets)
  JAEGER_URL      default http://localhost:16686
  NOTIFICATION_CONTAINER  default eshop-notification-api; the Mailpit guard reads its Smtp__Host
"""

import base64
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

GATEWAY = os.environ.get("GATEWAY_URL", "http://localhost:7000").rstrip("/")
MAILPIT = os.environ.get("MAILPIT_URL", "http://localhost:8025").rstrip("/")
ADMIN_EMAIL = os.environ.get("ADMIN_EMAIL", "admin@eshop.com")
ADMIN_PASSWORD = os.environ.get("ADMIN_PASSWORD", "Admin123!")
TIMEOUT = float(os.environ.get("SMOKE_TIMEOUT", "90"))
CHECK_TRACES = os.environ.get("SMOKE_CHECK_TRACES", "") not in ("", "0", "false")
JAEGER = os.environ.get("JAEGER_URL", "http://localhost:16686").rstrip("/")
NOTIFICATION_CONTAINER = os.environ.get("NOTIFICATION_CONTAINER", "eshop-notification-api")
SERVICES = ["EShop.ApiGateway", "EShop.Identity.API", "EShop.Catalog.API", "EShop.Basket.API",
            "EShop.Ordering.API", "EShop.Payment.API", "EShop.Notification.API"]

START = time.monotonic()


class StepFailed(Exception):
    pass


def log(msg):
    print(f"[{time.monotonic() - START:6.1f}s] {msg}", flush=True)


def call(method, url, body=None, token=None, expect=(200,)):
    """Send one request; return (status, parsed JSON or text). Raise StepFailed on an unexpected status."""
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            status, raw = resp.status, resp.read()
    except urllib.error.HTTPError as e:
        status, raw = e.code, e.read()
    except urllib.error.URLError as e:
        raise StepFailed(f"{method} {url}: {e.reason}") from e
    text = raw.decode("utf-8", "replace")
    try:
        payload = json.loads(text) if text else None
    except json.JSONDecodeError:
        payload = text
    if expect is not None and status not in expect:
        raise StepFailed(f"{method} {url} -> {status} (expected {expect}): {text[:400]}")
    return status, payload


def email_verified_claim(access_token):
    """The email_verified claim of a JWT, decoded without verifying it (the gateway already did)."""
    payload = access_token.split(".")[1]
    claims = json.loads(base64.urlsafe_b64decode(payload + "=" * (-len(payload) % 4)))
    return claims.get("email_verified")


def poll(what, fn, timeout=TIMEOUT, interval=1.0):
    """Call fn() until it returns a truthy value; fail after timeout."""
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            result = fn()
            if result:
                return result
        except StepFailed as e:  # transient answers while the async chain catches up
            last = e
        time.sleep(interval)
        interval = min(interval * 1.5, 5.0)
    raise StepFailed(f"timed out after {timeout:.0f}s waiting for {what}" + (f"; last: {last}" if last else ""))


def mailpit_messages(address):
    query = urllib.parse.quote(f'to:"{address}"')
    _, data = call("GET", f"{MAILPIT}/api/v1/search?query={query}&limit=50")
    return data.get("messages") or []


def require_mailpit():
    """Refuse to run unless Notification sends to Mailpit (docker-ci DC-49)."""
    if shutil.which("docker") is None:
        raise StepFailed("docker is not on PATH, so the Mailpit guard cannot check Notification's SMTP host")
    try:
        host = subprocess.run(["docker", "exec", NOTIFICATION_CONTAINER, "printenv", "Smtp__Host"],
                              capture_output=True, text=True, timeout=30, check=False).stdout.strip()
    except subprocess.TimeoutExpired as e:
        raise StepFailed(f"docker exec {NOTIFICATION_CONTAINER} timed out") from e
    if host != "mailpit":
        # The fix is described, not spelled out: a literal "..._PASSWORD=" here reads as a hard-coded credential to
        # secret scanners (SonarCloud flagged it on PR #86).
        raise StepFailed(f"{NOTIFICATION_CONTAINER} sends mail via '{host or '<unset>'}', not mailpit; this run would "
                         "send real email. Recreate it with the NOTIFICATION_SMTP_* variables pointed at Mailpit, as "
                         "docs/02-getting-started/docker-setup.md and the Docker Smoke workflow do.")
    log("notification sends to mailpit")


def check_traces(since_us):
    """Every service exported at least one span under its own service.name since the run started (DC-35)."""
    def missing():
        absent = []
        for service in SERVICES:
            query = urllib.parse.urlencode({"service": service, "start": since_us, "limit": 1})
            _, body = call("GET", f"{JAEGER}/api/traces?{query}")
            if not (body or {}).get("data"):
                absent.append(service)
        return absent

    # The exporter sends a batch every 5 s; allow a few batches.
    deadline = time.monotonic() + 60
    absent = missing()
    while absent and time.monotonic() < deadline:
        time.sleep(5)
        absent = missing()
    if absent:
        raise StepFailed(f"no spans in Jaeger since the run started for: {', '.join(absent)}")
    log(f"spans from all {len(SERVICES)} services reached Jaeger")


def main():
    run = uuid.uuid4().hex[:10]
    started_us = int(time.time() * 1_000_000)
    email = f"smoke-{run}@example.com"
    password = f"Smoke-{run}-Aa1!"

    log(f"gateway {GATEWAY}, mailpit {MAILPIT}, customer {email}")
    require_mailpit()
    poll("gateway /health/ready", lambda: call("GET", f"{GATEWAY}/health/ready")[0] == 200, timeout=120)

    # A protected route must refuse an anonymous caller at the gateway.
    call("GET", f"{GATEWAY}/api/v1/orders", expect=(401,))
    log("anonymous GET /api/v1/orders refused with 401")

    # 1. Sign-up, and a login before the address is confirmed
    _, reg = call("POST", f"{GATEWAY}/api/v1/auth/register",
                  {"email": email, "password": password, "firstName": "Smoke", "lastName": "Test"})
    user_id = reg["userId"]
    log(f"registered user {user_id}")

    def confirmation_link():
        for m in mailpit_messages(email):
            _, msg = call("GET", f"{MAILPIT}/api/v1/message/{m['ID']}")
            # The text part renders the link as "Confirm email (<url>)": match only URL-safe characters, or the
            # closing parenthesis becomes part of the token and confirmation answers Auth.InvalidToken.
            match = re.search(r"[?&]userId=([A-Za-z0-9%._~-]+)&token=([A-Za-z0-9%._~-]+)", msg.get("Text") or "")
            if match:
                return urllib.parse.unquote(match.group(1)), urllib.parse.unquote(match.group(2))
        return None

    # Soft email verification: an unconfirmed customer signs in at once; only placing an order needs the address.
    _, login = call("POST", f"{GATEWAY}/api/v1/auth/login", {"email": email, "password": password})
    user_token = login["accessToken"]
    if (login.get("user") or {}).get("emailConfirmed") is not False or email_verified_claim(user_token) is not False:
        raise StepFailed(f"an unconfirmed login should say emailConfirmed=false and email_verified=false: {login}")
    log("customer logged in before confirming (email_verified=false)")

    # 2. Browse and fill the basket
    _, page = call("GET", f"{GATEWAY}/api/v1/products?pageSize=50")
    product = next((p for p in page["items"] if (p.get("stockQuantity") or 0) > 0), None)
    if product is None:
        raise StepFailed("no product with stock in the public catalog (is the Catalog seed missing?)")
    log(f"picked product {product['sku']} ({product['id']})")

    call("POST", f"{GATEWAY}/api/v1/basket/{user_id}/items", {"productId": product["id"], "quantity": 1},
         token=user_token, expect=(204,))
    checkout_body = {"shippingAddress": {"street": "1 Smoke Street", "city": "Testville", "state": "Texas",
                                         "zipCode": "12345", "country": "US"}}

    # Checking out places an order, so it is refused until the address is confirmed; the basket survives.
    status, body = call("POST", f"{GATEWAY}/api/v1/basket/{user_id}/checkout", checkout_body,
                        token=user_token, expect=(403,))
    if (body or {}).get("errorCode") != "Auth.EmailNotConfirmed":
        raise StepFailed(f"an unconfirmed checkout answered {status} {body}")
    log("unconfirmed checkout refused with 403 Auth.EmailNotConfirmed")

    link_user, token = poll("the confirmation email", confirmation_link)
    if link_user != user_id:
        raise StepFailed(f"confirmation link names user {link_user}, expected {user_id}")
    log("confirmation email received")
    call("POST", f"{GATEWAY}/api/v1/auth/confirm-email", {"userId": user_id, "token": token})
    log("email confirmed")

    # The access token in hand is a snapshot; a refresh re-reads the user and says email_verified=true.
    _, refreshed = call("POST", f"{GATEWAY}/api/v1/auth/refresh-token", {"refreshToken": login["refreshToken"]})
    user_token = refreshed["accessToken"]
    if email_verified_claim(user_token) is not True:
        raise StepFailed(f"the refreshed token does not say email_verified=true: {email_verified_claim(user_token)}")
    log("token refreshed (email_verified=true)")

    _, checkout = call("POST", f"{GATEWAY}/api/v1/basket/{user_id}/checkout", checkout_body, token=user_token)
    log(f"checked out, checkoutId {checkout['checkoutId']}")

    # 3. The order appears (asynchronously, via BasketCheckedOut)
    def newest_order():
        _, orders = call("GET", f"{GATEWAY}/api/v1/users/{user_id}/orders?pageSize=1", token=user_token)
        items = orders.get("items") or []
        return items[0] if items else None

    order = poll("the order to appear", newest_order)
    order_id = order["id"]
    log(f"order {order_id} created, status {order['status']}")

    # 4. The admin settles the payment offline (the payment record arrives via OrderCreated)
    _, admin = call("POST", f"{GATEWAY}/api/v1/auth/login", {"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD})
    admin_token = admin["accessToken"]
    reference = f"smoke-{run}"

    def settle():
        status, _ = call("POST", f"{GATEWAY}/api/v1/payments/offline", {"orderId": order_id, "reference": reference},
                         token=admin_token, expect=None)
        if status in (404, 409):  # the payment record is not there yet
            return None
        if status != 200:
            raise StepFailed(f"offline settlement answered {status}")
        return True

    poll("the payment record to accept an offline settlement", settle)
    log("payment settled offline")

    def order_status(wanted):
        def check():
            _, o = call("GET", f"{GATEWAY}/api/v1/orders/{order_id}", token=user_token)
            return o if o.get("status") == wanted else None
        return check

    poll("the order to become Paid", order_status("Paid"))
    log("order is Paid")

    # 5. Ship and deliver
    call("POST", f"{GATEWAY}/api/v1/orders/{order_id}/ship", token=admin_token, expect=(204,))
    call("POST", f"{GATEWAY}/api/v1/orders/{order_id}/deliver", token=admin_token, expect=(204,))
    poll("the order to become Delivered", order_status("Delivered"), timeout=15)
    log("order shipped and delivered")

    # 6. The customer's emails
    def expected_emails():
        subjects = sorted(m.get("Subject", "") for m in mailpit_messages(email))
        has_confirm = any("confirm" in s.lower() for s in subjects)
        has_shipped = any("shipped" in s.lower() for s in subjects)
        return subjects if has_confirm and has_shipped else None

    subjects = poll("the confirmation and shipped emails", expected_emails)
    log(f"emails for the customer ({len(subjects)}): " + " | ".join(subjects))

    if CHECK_TRACES:
        check_traces(started_us)

    log("SMOKE PASSED")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except StepFailed as e:
        log(f"SMOKE FAILED: {e}")
        sys.exit(1)
