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

## 3. Router and firewall

1. Give the machine a fixed LAN address (a DHCP reservation in the router). Read the address from the adapter that has a
   default gateway, `Get-NetIPConfiguration | Where-Object IPv4DefaultGateway`, not from a virtual adapter
   (VirtualBox's `192.168.56.1`, WSL's `vEthernet`). A forward to any other device's address fails silently.
2. Forward TCP **80** and **443** to that address. Nothing else.
3. Windows: Docker Desktop normally adds its own firewall rule when it is installed. If the test in step 6 times out,
   allow the ports explicitly (elevated PowerShell):

   ```powershell
   New-NetFirewallRule -DisplayName "EShop HTTP/HTTPS" -Direction Inbound -Protocol TCP -LocalPort 80,443 -Action Allow
   ```
4. Nothing else on the machine may listen on 80 or 443. Check with
   `Get-NetTCPConnection -State Listen -LocalPort 80,443` (IIS and other `http.sys` users show as `System`).

## 4. Start the stack

```bash
docker compose -f docker-compose.yml -f docker-compose.override.selfhost.yml \
  --profile sandbox --profile selfhost up -d --build
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
  --profile sandbox --profile selfhost up -d --build
```

## Stopping public access

`docker compose … stop nginx` takes the site offline at once; remove the router's port forwards to close it for
good. The rest of the stack keeps running locally.

---

## Known limitations

- **Visitors may share one client IP.** Docker Desktop on Windows forwards published ports through its own proxy,
  which may hide the visitor's address from nginx (locally every request arrives as `172.18.0.1`). If it does, every
  visitor shares the gateway's per-IP rate limits, including Identity's sign-in limit of 5 per minute. To check,
  open the site from a phone on mobile data and look at the last lines of `docker logs eshop-nginx`: the first field
  is the address nginx saw. If it is `172.x`, the fix is to run the stack on Docker Engine inside a Linux VM or WSL
  distribution (or on a Linux server, see below), not on Docker Desktop.
- **Email.** The Sandbox profile delivers every email to Mailpit, so real visitors get none. Checkout needs a
  confirmed email address. Either point Notification at a real SMTP server (`NOTIFICATION_SMTP_*` in `.env`, see the
  Gmail example there) or confirm users from the admin panel (**Users → Mark email confirmed**). The storefront has no
  `/confirm-email` or `/reset-password` page yet, so emailed links lead to a 404 until it does.
- **Payments are Stripe test mode** (the Sandbox profile): no real money moves.

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

## On a free cloud server instead

If the machine cannot stay on, Oracle Cloud's Always Free tier offers an ARM VM (up to 4 cores and 24 GB), the only
free tier large enough for the stack (about 2.2 GB idle with observability). The images build on ARM as they are.
The steps above stay the same, minus the router, and a Linux host also removes the client-IP limitation.

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
