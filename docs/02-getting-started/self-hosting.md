# Self-Hosting on a Home Machine

How to put the shop on the internet for free from a machine at home, under a free DuckDNS name
(`https://<name>.duckdns.org`), with a real Let's Encrypt certificate. No domain has to be bought.

```
browser ──https──▶ router :443 ──▶ nginx ──▶ ui (Next.js :3000) ──▶ api-gateway :8080 ──▶ services
                                   (eshop-nginx)  (eshop-ui)
```

Only nginx is reachable from outside. The browser never calls the gateway: the Next.js server does, with the visitor's
token from an httpOnly cookie. Everything else stays on the Docker networks, and the ports the stack publishes for
local tooling are bound to `127.0.0.1`.

Everything lives in `docker-compose.override.selfhost.yml`, on top of the `sandbox` profile:

| Service | Image | Job |
|---|---|---|
| `ui` | built from `ui/Dockerfile` | the Next.js standalone server |
| `nginx` | `nginx:1.30.5-alpine` | TLS, HTTP→HTTPS redirect, proxy to `ui`; the only published ports (80, 443) |
| `certbot` | `certbot/certbot` | renews the certificate through nginx's webroot twice a day |
| `duckdns` | `lscr.io/linuxserver/duckdns` | keeps the DuckDNS record on your current public IP |

Do **not** add `docker-compose.override.public.yml`: it publishes RabbitMQ, Prometheus, Grafana, Seq and Jaeger.

---

## Requirements

- The machine runs the stack already (see [Docker Setup](docker-setup.md)) and stays on: turn off sleep.
- **Prefer a Linux machine with Docker Engine** (an Ubuntu server, an old laptop, a cloud VM; see
  [On a Linux server](#on-a-linux-server-recommended)). There every visitor reaches the shop with their own address.
  Docker Desktop on Windows works too, but hides the visitors' addresses (see [Known limitations](#known-limitations)).
- Your connection has a public IPv4 address and the router can forward ports 80 and 443. If it cannot (CGNAT, or
  the ISP blocks the ports), see [Without port forwarding](#without-port-forwarding).
- A DuckDNS account: sign in at <https://www.duckdns.org>, add a subdomain, copy the token at the top of the page.

---

## 1. Before anything is public: change the admin password

The Sandbox environment seeds `admin@eshop.com` with `IDENTITY_SEED_ADMIN_PASSWORD`, and the value in
`.env.example` (`Admin123!`) is public. Identity creates that admin only when it does not exist yet, so:

- On a fresh database, set a strong `IDENTITY_SEED_ADMIN_PASSWORD` in `.env` before the first start.
- On an existing one, changing the variable does nothing: sign in as the admin and change the password on
  **My account** before opening any port.

## 2. Configure `.env`

Fill in the self-hosting block (the template is at the end of `.env.example`):

```dotenv
SELFHOST_DOMAIN=myshop.duckdns.org
DUCKDNS_SUBDOMAIN=myshop
DUCKDNS_TOKEN=<the token from duckdns.org>
```

The override also points the password-reset and email-confirmation links at `https://$SELFHOST_DOMAIN/…`.

**Card payments** need a Stripe account in test mode (free; <https://dashboard.stripe.com/test/apikeys>). Set
`STRIPE_SECRET_KEY` (`sk_test_…`) and `STRIPE_PUBLISHABLE_KEY` (`pk_test_…`) from the same account. The secret key
stays with the Payment service; the publishable key goes to the `ui` container, where the card form (Stripe's Payment
Element) uses it in the visitor's browser. Stripe tells the shop that a payment went through by webhook, and at home
the `stripe` profile receives it: its `stripe-listener` container prints a `whsec_…` secret on its first start
(`docker logs eshop-stripe-listener`), which goes in `STRIPE_WEBHOOK_SECRET`. Without the listener a paid order stays
"Confirming your payment…". In test mode the payment page shows the test cards: `4242 4242 4242 4242` succeeds and
`4000 0000 0000 0002` is declined, with any future date and any CVC.

## 3. Router and firewall

1. Give the machine a fixed LAN address (a DHCP reservation in the router). Read the address from the adapter that has a
   default gateway, `Get-NetIPConfiguration | Where-Object IPv4DefaultGateway`, not from a virtual adapter
   (VirtualBox's `192.168.56.1`, WSL's `vEthernet`). A forward to any other device's address fails silently.
2. Forward TCP **80** and **443** to that address. Nothing else.
3. Linux with `ufw` enabled: `sudo ufw allow 80,443/tcp`. Windows: Docker Desktop normally adds its own firewall rule
   when it is installed. If the test in step 6 times out, allow the ports explicitly (elevated PowerShell):

   ```powershell
   New-NetFirewallRule -DisplayName "EShop HTTP/HTTPS" -Direction Inbound -Protocol TCP -LocalPort 80,443 -Action Allow
   ```
4. Nothing else on the machine may listen on 80 or 443. Check with
   `Get-NetTCPConnection -State Listen -LocalPort 80,443` (IIS and other `http.sys` users show as `System`).

## 4. Start the stack

```bash
docker compose -f docker-compose.yml -f docker-compose.override.selfhost.yml \
  --profile sandbox --profile selfhost --profile stripe up -d --build
```

Without a certificate nginx starts in HTTP-only mode: it answers the ACME challenge and returns
`503 HTTPS certificate not issued yet` for everything else (`docker logs eshop-nginx` says so too).

Check that DuckDNS points at you: `nslookup myshop.duckdns.org` must return your public IP
(compare with <https://ifconfig.me>). The `duckdns` container updates it within five minutes;
`docker logs eshop-duckdns` shows the result.

## 5. Issue the first certificate (once)

Try against Let's Encrypt's staging server first, so a mistake does not count against the production rate limits. A
successful dry run also proves that port 80 is reachable from the internet. From Git Bash, prefix each command with
`MSYS_NO_PATHCONV=1`, or it rewrites `/var/www/certbot` into `C:/Program Files/Git/var/www/certbot` and certbot
fails with `does not exist or is not a directory`; PowerShell needs nothing:

```bash
COMPOSE="docker compose -f docker-compose.yml -f docker-compose.override.selfhost.yml --profile sandbox --profile selfhost"

$COMPOSE run --rm --entrypoint certbot certbot certonly --webroot -w /var/www/certbot \
  -d myshop.duckdns.org --register-unsafely-without-email --agree-tos --dry-run
```

When that reports success, run it for real (without `--dry-run`). Use `--email you@example.com` instead of
`--register-unsafely-without-email` if you want account notices from Let's Encrypt.

```bash
$COMPOSE run --rm --entrypoint certbot certbot certonly --webroot -w /var/www/certbot \
  -d myshop.duckdns.org --register-unsafely-without-email --agree-tos
$COMPOSE restart nginx
```

nginx would pick the certificate up by itself within an hour; the restart just does it now. From then on the
`certbot` service renews it automatically and nginx reloads every hour, so nothing else needs doing.

To check that renewal will work, without waiting for it:

```bash
$COMPOSE run --rm --entrypoint certbot certbot renew --webroot -w /var/www/certbot --dry-run --no-random-sleep-on-renew
```

Without `--no-random-sleep-on-renew` certbot first waits up to 8 minutes in silence, which looks like a hang.

## 6. Check it from outside

Use a phone on **mobile data**, not your Wi-Fi: many routers cannot reach their own public address from inside
(no NAT hairpinning), so a test from home can fail while the site works for everyone else.

- `https://myshop.duckdns.org` shows the shop with a valid padlock; `http://` redirects to `https://`.
- Register, sign in, add to and remove from the cart, change your name on **My account**.
- The bare IP (`https://<your public IP>`) gets no answer: only the configured name is served.

---

## Updating

```bash
git pull
docker compose -f docker-compose.yml -f docker-compose.override.selfhost.yml \
  --profile sandbox --profile selfhost --profile stripe up -d --build
```

## Stopping public access

`docker compose … stop nginx` takes the site offline at once; remove the router's port forwards to close it for
good. The rest of the stack keeps running locally.

---

## Known limitations

- **On Docker Desktop, all visitors share one client IP.** Docker Desktop on Windows forwards published ports
  through its own proxy, so nginx sees every connection as coming from `172.18.0.1`. This is confirmed: on the first
  real run (2026-10-06) even Let's Encrypt's validation servers, which connect from the internet, were logged as
  `172.18.0.1`, and Docker Desktop has no setting that changes it. Every visitor therefore shares every per-IP limit:
  with the services' defaults, 100 API calls and 5 sign-ins a minute for the whole site, and ten mistyped passwords
  from anyone blocked sign-in for everyone for 30 minutes (Identity's brute-force protection, hit on 2026-10-10).
  For a demo from Docker Desktop, raise the per-IP limits in `.env` (the block is in `.env.example`); on a Linux
  host leave them unset. The per-account lockout (5 failed sign-ins) applies either way.

  | Variable | Default (Linux) | Docker Desktop | What it sets |
  |---|---|---|---|
  | `SELFHOST_GLOBAL_PERMIT_LIMIT` | 100 | 2000 | API calls per minute, at the gateway and in every service |
  | `SELFHOST_LOGIN_PERMIT_LIMIT` | 5 | 60 | Sign-in, resend-confirmation, forgot- and reset-password calls per minute |
  | `SELFHOST_AUTH_PERMIT_LIMIT` | 10 | 120 | Register, refresh, revoke and confirm-email calls per minute |
  | `SELFHOST_MAX_FAILED_LOGINS_PER_IP` | 10 | 200 | Failed sign-ins before the address is blocked |
  | `SELFHOST_IP_BLOCK_MINUTES` | 30 | 5 | How long that block lasts |
  | `SELFHOST_SEARCH_PERMIT_LIMIT` | 30 | 600 | Catalog searches per minute |

  A block that is already in place stays until it expires. To lift it at once, delete it from Redis:
  `docker exec eshop-redis redis-cli -a "<REDIS_PASSWORD>" --no-auth-warning del bf_protection:ip_block:172.18.0.1 bf_protection:ip_attempts:172.18.0.1`.
  The fix is a Linux host ([below](#on-a-linux-server-recommended)). Check it with `docker logs eshop-nginx --tail 5`:
  the first field is the address nginx saw.
- **Email.** The Sandbox profile delivers every email to Mailpit, so real visitors get none. Checkout needs a
  confirmed email address. Either point Notification at a real SMTP server (`NOTIFICATION_SMTP_*` in `.env`, see the
  Gmail example there) or confirm users from the admin panel (**Users → Mark email confirmed**). The emailed links
  open the storefront's `/confirm-email` and `/reset-password` pages; `/forgot-password` sends a reset link, and the
  account page can send a new confirmation link.
- **Payments are Stripe test mode** (the Sandbox profile): no real money moves, and only cards are offered.

---

## Without port forwarding

If the router cannot forward ports, keep the `ui` service and replace nginx and certbot with a tunnel; all of these are
free and need no domain:

| Option | Address | Notes |
|---|---|---|
| Tailscale Funnel | `https://<machine>.<tailnet>.ts.net` | stable name, HTTPS included |
| Cloudflare quick tunnel | random `*.trycloudflare.com` | new address on every restart |
| ngrok (free static domain) | `*.ngrok-free.app` | visitors first see ngrok's warning page |

The tunnel then terminates TLS and forwards to `ui:3000`. Set `TRUST_PROXY_HEADERS` on `ui` only if the tunnel sets
`X-Forwarded-For` itself.

## On a Linux server (recommended)

Any Linux machine with Docker Engine runs the same compose files, and nginx sees each visitor's real address with no
extra setup: Docker Engine publishes ports through the kernel, not through a proxy. An Ubuntu Server laptop or mini
PC at home is enough; the stack uses about 2.2 GB of RAM idle with observability, so 8 GB is comfortable.

1. Install Docker Engine (not Docker Desktop) from Docker's apt repository, as in
   <https://docs.docker.com/engine/install/ubuntu/>, then `sudo usermod -aG docker $USER` and sign in again.
2. A laptop must not sleep with its lid closed: set `HandleLidSwitch=ignore` (and `HandleLidSwitchExternalPower=ignore`)
   in `/etc/systemd/logind.conf`, then `sudo systemctl restart systemd-logind`.
3. `git clone` the repository, copy your `.env` (without the Docker Desktop `SELFHOST_*` limits), and follow steps
   1 to 6 above. In step 3 the router forwards 80 and 443 to this machine's LAN address
   (`ip -4 addr show scope global`), and only one machine can receive them, so stop the stack on the other one.
4. Moving an existing shop: stop it on the old machine, archive every `eshop-*` volume
   (`docker run --rm -v <volume>:/v:ro -v "$PWD":/b busybox:1.36 tar czf /b/<volume>.tgz -C /v .`), copy the
   archives over, create each volume on the new machine and unpack into it
   (`docker run --rm -v <volume>:/v -v "$PWD":/b busybox:1.36 tar xzf /b/<volume>.tgz -C /v`). With
   `eshop-certbot-etc` restored, no new certificate has to be issued.

Every container has `restart: unless-stopped`, so after a reboot the shop comes back once Docker starts
(`sudo systemctl enable docker`, the default on Ubuntu).

If no machine can stay on, Oracle Cloud's Always Free tier offers an ARM VM (up to 4 cores and 24 GB), the only free
tier large enough for the stack. The images build on ARM as they are. The steps are the same, minus the router.

---

## Troubleshooting

| Symptom | Cause |
|---|---|
| `503 HTTPS certificate not issued yet` | Step 5 has not succeeded yet. |
| certbot: `Timeout during connect` / `Connection refused` | Port 80 is not forwarded, a firewall drops it, or DuckDNS still points at an old IP. |
| Works on Wi-Fi only by IP, not by name | No NAT hairpinning in the router; test from mobile data. |
| Sign-in "does nothing" | The site was opened over `http://`: the auth cookies are `Secure` and need HTTPS. |
| Every form fails after a proxy change | Server actions compare `Origin` with the forwarded host: nginx must pass `Host $host`. |

---

## Related Documents

- [Docker Setup](docker-setup.md)
- [CI/CD Workflow](../07-development-workflow/ci-cd-workflow.md)

---

**Last Updated**: 2026-10-06
