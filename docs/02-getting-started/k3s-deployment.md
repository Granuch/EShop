# Running EShop on k3s (operations runbook)

How the public shop runs, and what to do day to day: check it, release, roll back, restore data, rotate a secret,
and recover from the problems already met. The manifests are described in [deploy/README.md](../../deploy/README.md)
and the pipeline in [ci-cd-workflow.md](../07-development-workflow/ci-cd-workflow.md); this page is the operator's
side.

Examples use a kubeconfig context named `granuch-server` and the Flux CLI 2.10; the public name is
`granuch-shop.duckdns.org`. `<server>` stands for the server's LAN or Tailscale address.

---

## How it fits together

- **One k3s node** at home runs everything in namespace `eshop`: the 7 .NET services, the Next.js ui, 5 PostgreSQL,
  Redis, RabbitMQ, the monitoring stack, a DuckDNS updater and the Stripe CLI listener. cert-manager and Flux run in
  their own namespaces.
- **Only the storefront is public**: the router forwards 80 and 443 to the node, where Traefik terminates TLS (a Let's
  Encrypt certificate cert-manager renews) and sends everything to `ui`, which alone calls `api-gateway`.
- **Releases are pulled, never pushed.** A green merge to `master` makes CI publish the images and the rendered
  manifests to GHCR and move the artifact's `production` tag; Flux polls that tag every minute and applies it. Nothing
  in GitHub can reach the server.

## Is it healthy?

```bash
flux get kustomizations --context granuch-server       # eshop-addons, eshop-infra, eshop-apps: Ready True
kubectl --context granuch-server -n eshop get pods     # everything Running 1/1, the CronJob pods Completed
kubectl --context granuch-server -n eshop get certificate eshop-tls
curl -s -o /dev/null -w '%{http_code}\n' https://granuch-shop.duckdns.org/api/health   # 200
```

The operator UIs answer on the server's own address, from the home network or Tailscale only:

| UI | Address |
|---|---|
| Grafana | `http://<server>:30300` |
| Seq (logs) | `http://<server>:30341` |
| Jaeger (traces) | `http://<server>:30686` |
| Prometheus | `http://<server>:30090` |
| RabbitMQ | `http://<server>:31672` |
| RedisInsight | `http://<server>:30540` |
| Redis Commander | `http://<server>:30081` |

Logs of one service without Seq: `kubectl -n eshop logs deploy/<name> --since=15m`.

## Releasing and rolling back

- **Release:** merge to `master`. Watch it with `flux get kustomizations --watch`; to apply at once instead of within a
  minute, `flux reconcile kustomization eshop-apps --with-source`.
- **What is live:** `flux get sources oci` shows the digest; `kubectl -n eshop get deploy -o wide` shows the image
  tags (`sha-<commit>`).
- **Roll back:** `gh workflow run Rollback -f commit=<full SHA of an earlier master commit>`. Database migrations are
  not undone, so roll back only to code that works with the current schema.
- **Hold releases** (maintenance, a data restore): `flux suspend kustomization eshop-apps` (and `eshop-infra` for the
  databases); `flux resume …` hands control back. While suspended, Flux leaves those objects alone.
- **Never fix a Flux-managed object with `kubectl edit`/`patch` and expect it to stay.** Flux removes such changes at
  the next reconcile. Suspend first, or make the change in `deploy/` and release it.

## Backups and restore

`db-backup` (a CronJob in `eshop`) dumps all five databases every night at 03:15 Kyiv time with `pg_dump -Fc`, into a
folder per day on the `db-backups` volume, and keeps the seven newest days. A missed night runs when the server comes
back, within 12 hours.

```bash
kubectl -n eshop get cronjob db-backup                  # LAST SCHEDULE
kubectl -n eshop logs job/<newest db-backup job>        # lists the five dumps
kubectl -n eshop create job --from=cronjob/db-backup db-backup-manual   # a backup now
```

These dumps live on the same disk as the databases: they cover mistakes and failed migrations, not a dead disk. Copy
them elsewhere for that (the server's own backup job can pick up the volume's folder).

**Restore one database** (here `catalog`), with its service stopped so nothing writes meanwhile:

```bash
flux suspend kustomization eshop-apps
kubectl -n eshop scale deploy/catalog-api --replicas=0

# A throwaway pod that mounts the backup volume, to read the dumps.
kubectl -n eshop run backup-shell --restart=Never --image=busybox:1.36 --overrides='{"spec":{"securityContext":
  {"runAsNonRoot":true,"runAsUser":70},"containers":[{"name":"backup-shell","image":"busybox:1.36",
  "command":["sleep","3600"],"volumeMounts":[{"name":"b","mountPath":"/backups"}]}],
  "volumes":[{"name":"b","persistentVolumeClaim":{"claimName":"db-backups"}}]}}'
kubectl -n eshop exec backup-shell -- ls /backups                       # pick a day

# Recreate the database and stream the dump into it.
kubectl -n eshop exec catalog-postgres-0 -- dropdb -U postgres --if-exists eshop_catalog
kubectl -n eshop exec catalog-postgres-0 -- createdb -U postgres eshop_catalog
kubectl -n eshop exec backup-shell -- cat /backups/<day>/catalog.dump \
  | kubectl -n eshop exec -i catalog-postgres-0 -- pg_restore -U postgres -d eshop_catalog --no-owner --no-privileges

kubectl -n eshop delete pod backup-shell
flux resume kustomization eshop-apps                    # scales catalog-api back up
```

`kubectl cp backup-shell:/backups/<day>/<db>.dump <local file>` takes a copy off the server. The move from the old PC
stack used the same `pg_dump`/`pg_restore` pair and compared exact row counts per table before and after.

## Secrets

All credentials are one Secret, `eshop-secrets`, committed SOPS-encrypted as
`deploy/overlays/server/infra/secrets.enc.yaml`; only the server holds the age key that decrypts it (keep an offline
copy of that key: without it the file cannot be changed or recovered).

- **Change a value:** on a machine with the key, `SOPS_AGE_KEY_FILE=<key> sops deploy/overlays/server/infra/secrets.enc.yaml`,
  commit, merge. Flux applies the new Secret; restart what reads it
  (`kubectl -n eshop rollout restart deploy/<name>`), because environment variables are read at start.
- **Database passwords** are also stored inside each PostgreSQL data directory. Changing one in the Secret alone
  locks the services out: run `ALTER USER postgres PASSWORD '…'` in the pod first.
- `validate.sh` (CI's `manifests` job) refuses a commit in which the file holds an unencrypted value.

## Certificate

cert-manager renews `eshop-tls` about 30 days before it expires. If issuance fails, check from outside first that
port 80 reaches the server (an online HTTP checker against `http://granuch-shop.duckdns.org`), then re-issue at once
with `kubectl -n eshop delete certificate eshop-tls` (the Ingress recreates it). Let's Encrypt allows 5 failed
validations per name per hour, so do not loop.

## Problems already met

| Symptom | Cause | Fix |
|---|---|---|
| PostgreSQL never ready; `gosu` at 100 % CPU; `could not signal for checkpoint: Permission denied`; kernel log `apparmor="DENIED" operation="signal"` | Ubuntu 26.04's AppArmor 5 stacks containerd's default profile, which then rejects signals between a container's processes | Pods use the node's `eshop-container` profile (`deploy/components/apparmor`); a new node needs that profile file before EShop starts |
| RabbitMQ crash-loops after a restart: `Cookie file … must be accessible by owner only` | The kubelet re-applied `fsGroup` and made the cookie group-readable | `fsGroupChangePolicy: OnRootMismatch` on every pod (enforced by `validate.sh`); fix an existing cookie with `chmod 600` from a pod running as uid 999 |
| Site and certificate time out from outside, fine from home by IP | The router's 80/443 forward points at another machine | Virtual Servers on the router: 80 and 443 to the server |
| A deploy job failed with no steps and no log | The `production` environment's rules changed while the job waited | `gh run rerun <id> --failed` |
| A merged Dependabot update was never deployed | Pushes by the workflow token start no workflow | `ci-catch-up.yml` starts CI for such a commit within 30 minutes; or `gh workflow run ci.yml --ref master` |

## Power and the home server

The server runs on battery during outages and shuts down cleanly at a low charge; every workload restarts by itself
when it comes back (k3s is a system service, Flux re-applies, StatefulSets keep their volumes). The server's own power
management stops Docker containers at a low charge; stopping k3s's pods too is handled there, not in this repository.
