# deploy/: Kubernetes manifests

The whole EShop stack as Kustomize manifests: the seven .NET components, the Next.js storefront, one PostgreSQL per
service, Redis, RabbitMQ and the full monitoring stack. It replaces `k8s/`, which only ran on Docker Desktop.

```
deploy/
├── base/
│   ├── infra/        5 × PostgreSQL, Redis, RabbitMQ (StatefulSets with volumes)
│   ├── apps/         identity, catalog, basket, ordering, payment, notification, api-gateway, ui
│   └── monitoring/   otel-collector, Jaeger, Prometheus, Grafana, Seq, exporters, RedisInsight, Redis Commander
├── overlays/
│   ├── server/       the home server (k3s): namespace eshop
│   │   ├── infra/    namespace + infra, and secrets.enc.yaml (SOPS)
│   │   └── apps/     apps + monitoring + public edge (Ingress, TLS, DuckDNS, Stripe listener, LAN-only NodePorts)
│   └── local/        any test cluster: namespace eshop-local, Mailpit, throwaway credentials
├── render.sh         renders an overlay to plain YAML (what CI publishes)
└── validate.sh       renders both overlays and checks them (CI's `manifests` job)
```

Service names match `docker-compose.yml`, so `infrastructure/` (Prometheus, OTel collector, Grafana, Postgres tuning)
and `monitoring/prometheus-alerts.yml` are used as they are, through `configMapGenerator`. Those files live outside
`deploy/`, which is why rendering needs `--load-restrictor LoadRestrictionsNone`; `render.sh` passes it.

## How it reaches the server

CI renders `overlays/server` with the images pinned to the commit (`sha-<commit>`) and publishes the result as an OCI
artifact; Flux on the server applies `infra/` first, then `apps/`. Nothing in GitHub can reach the server. See
`docs/07-development-workflow/ci-cd-workflow.md`.

Only the storefront is public: `granuch-shop.duckdns.org` → Traefik (TLS from Let's Encrypt through cert-manager) →
`ui` → `api-gateway`. The gateway and every other service are reachable only inside the cluster. The operator UIs have
fixed NodePorts on the server's own address, which the router does not forward, so they answer on the home LAN and
over Tailscale only:

| UI | Port | Login |
|---|---|---|
| Grafana | 30300 | `admin`, `grafana-admin-password` |
| Seq | 30341 | `admin`, `seq-admin-password` (first start only; an existing volume keeps its users) |
| Jaeger | 30686 | none |
| Prometheus | 30090 | none |
| RabbitMQ | 31672 | `rabbitmq-username` / `rabbitmq-password` |
| RedisInsight | 30540 | none |
| Redis Commander | 30081 | none |

## Secrets

Every credential is in one Secret, `eshop-secrets`. On the server it is `overlays/server/infra/secrets.enc.yaml`,
encrypted with [SOPS](https://github.com/getsops/sops) for an [age](https://github.com/FiloSottile/age) key
(`.sops.yaml` holds the public key). Only the values are encrypted, so a diff still shows which key changed. The
private key exists only on the server and in the owner's offline backup; Flux decrypts the file when it applies it.
`validate.sh` fails if the file ever holds an unencrypted value.

- Change values (needs the private key): `SOPS_AGE_KEY_FILE=<key> sops deploy/overlays/server/infra/secrets.enc.yaml`
- Replace the whole file from a plaintext Secret (public key only):
  `sops encrypt --filename-override deploy/overlays/server/infra/secrets.enc.yaml plain.yaml > deploy/overlays/server/infra/secrets.enc.yaml`,
  then delete `plain.yaml`.
- Keys: the five `*-db-password`s, `redis-password`, `rabbitmq-username`, `rabbitmq-password`, `jwt-secret-key`,
  `internal-service-api-key`, `seed-admin-password`, `smtp-username`, `smtp-password`, `smtp-from-email`,
  `stripe-secret-key` (test mode only), `stripe-publishable-key`, `stripe-webhook-secret`, `seq-admin-password`,
  `grafana-admin-password`, `duckdns-token`.

Values go into connection strings, so keep them free of `;`, `=` and quotes (hex is safest).

## Trying it on a test cluster

```bash
deploy/overlays/local/make-secrets.sh                      # writes the gitignored secrets.env
deploy/render.sh local /tmp/eshop && kubectl apply --server-side -f /tmp/eshop/local.yaml
kubectl -n eshop-local get pods -w
kubectl -n eshop-local port-forward svc/ui 3000:3000        # the shop
kubectl -n eshop-local port-forward svc/mailpit 8025:8025   # captured email
kubectl delete namespace eshop-local                        # removes everything, volumes included
```

Plain `kubectl apply -k` fails on the configMapGenerators that read `infrastructure/`, because it cannot lift the
load restrictor; `render.sh` passes it. Apply with `--server-side`: client-side apply copies each object into a
`last-applied-configuration` annotation limited to 256 KB, and the Grafana dashboards ConfigMap is already ~190 KB.

## Changing something

- Run `deploy/validate.sh` before pushing; CI runs it on every pull request.
- The five PostgreSQL blocks in `base/infra/postgres.yaml` and the five exporters in `base/monitoring/exporters.yaml`
  are identical apart from names; change all five together.
- A setting that differs between server and local goes into one of the per-component ConfigMaps in
  `base/apps/kustomization.yaml` and is merged by the overlay (`behavior: merge`). Shared settings are in
  `eshop-config`.
- A new image tag here usually also belongs in `docker-compose.yml`; Dependabot proposes both.
