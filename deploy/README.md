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
├── components/
│   └── apparmor/     puts every pod under the node's eshop-container AppArmor profile (server only)
├── flux/             sync.yaml (applied once on the server) and addons/ (cert-manager)
├── render.sh         renders an overlay to plain YAML (what CI publishes)
└── validate.sh       renders both overlays and checks them (CI's `manifests` job)
```

Service names match `docker-compose.yml`, so `infrastructure/` (Prometheus, OTel collector, Grafana, Postgres tuning)
and `monitoring/prometheus-alerts.yml` are used as they are, through `configMapGenerator`. Those files live outside
`deploy/`, which is why rendering needs `--load-restrictor LoadRestrictionsNone`; `render.sh` passes it.

## How it reaches the server

CI renders `overlays/server` (plus `flux/addons`, which installs cert-manager) with the images pinned to the commit
(`sha-<commit>`) and publishes the result as the OCI artifact `ghcr.io/granuch/eshop-deploy:sha-<commit>`. The
deploy job then moves its `production` tag. Flux on the server polls that tag (`flux/sync.yaml`) and applies
`addons/` and `infra/`, then `apps/`. Nothing in GitHub can reach the server. See
`docs/07-development-workflow/ci-cd-workflow.md`.

### Setting up the server (once)

On a fresh k3s node, from a machine whose kubeconfig reaches it (context `granuch-server` here):

```bash
flux check --pre
flux install --version=v2.10.0                       # source, kustomize, helm and notification controllers
# On the server itself, so the private key never leaves it:
sudo k3s kubectl -n flux-system create secret generic sops-age \
  --from-file=age.agekey=<path to the age key>
kubectl apply -f deploy/flux/sync.yaml
flux get kustomizations --watch                      # eshop-addons, eshop-infra, eshop-apps
```

Day to day: `flux get kustomizations`, `flux get sources oci`, `flux logs --level=error`, and
`flux reconcile kustomization eshop-apps --with-source` to apply at once instead of waiting a minute.
`flux suspend kustomization eshop-apps` stops Flux from changing the apps (for maintenance, a data restore);
`flux resume …` hands control back. Upgrading Flux: `flux install --version=<new>` with the same flags.

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

## AppArmor on Ubuntu 26.04

On Ubuntu 26.04 (AppArmor 5) containerd's default AppArmor profile cannot work: container processes run under a
stacked label (`cri-containerd.apparmor.d//&unconfined`) that the profile does not accept as a signal peer, so one
process in a container cannot signal another. PostgreSQL never finished starting (`could not signal for checkpoint:
Permission denied`, after `gosu` spun at 100 % CPU), and the kernel log showed `apparmor="DENIED" operation="signal"`.
The home server therefore loads its own profile, `/etc/apparmor.d/eshop-container` (containerd's default plus that
label), and `components/apparmor` makes every pod in the server overlay use it. A new node needs that file first, or
the pods do not start. PostgreSQL also runs as its own user (uid 70), so its entrypoint never needs `gosu`.

## Trying it on a test cluster

```bash
deploy/overlays/local/make-secrets.sh                      # writes the gitignored secrets.env
deploy/render.sh local /tmp/eshop && kubectl apply --server-side -f /tmp/eshop/local.yaml
kubectl -n eshop-local get pods -w
kubectl -n eshop-local port-forward svc/ui 3000:3000        # the shop
kubectl -n eshop-local port-forward svc/mailpit 8025:8025   # captured email
kubectl delete namespace eshop-local                        # removes everything, volumes included
```

On the home server, render it with the AppArmor component (a throwaway overlay with `resources: [../local]` and
`components: [../../components/apparmor]`), or Postgres will not start. With the port-forwards above plus
`svc/jaeger 16686:16686`, `.github/scripts/smoke_e2e.py` runs against it: set `KUBE_NAMESPACE=eshop-local`
(and `KUBE_CONTEXT`), `ADMIN_EMAIL=admin@eshop.local` and `ADMIN_PASSWORD` to `seed-admin-password` from
`secrets.env`. That is how the stack was first verified on the server (2026-11-18, smoke passed).

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
