#!/usr/bin/env bash
# Checks both overlays: they render, every object matches its Kubernetes or CRD schema, the committed server
# Secret is still encrypted, and every workload sets the basics (memory limit, probes, no :latest).
# CI runs this on every pull request; run it locally before pushing a change under deploy/.
#
# Needs kubeconform on PATH (or $KUBECONFORM), Python 3 with PyYAML ($PYTHON, default python3) and a kustomize
# (see render.sh).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
kubeconform="${KUBECONFORM:-kubeconform}"
python="${PYTHON:-python3}"

# 1. The committed server Secret must be SOPS-encrypted: every value under stringData is ENC[...], and the
#    file carries SOPS metadata. A decrypted copy committed by mistake fails here before it is pushed further.
secret="$root/deploy/overlays/server/infra/secrets.enc.yaml"
grep -q '^sops:' "$secret" || { echo "FAIL: $secret has no sops metadata (not encrypted?)"; exit 1; }
plain="$(awk '/^stringData:/{s=1;next} /^[^ ]/{s=0} s && /^ +[a-z0-9-]+:/ && $0 !~ /ENC\[/' "$secret")"
if [ -n "$plain" ]; then
  echo "FAIL: unencrypted values in $secret:"; echo "$plain" | cut -d: -f1
  exit 1
fi

# 2. Render both overlays. The local one needs throwaway credentials.
local_secrets="$root/deploy/overlays/local/secrets.env"
created_local=0
if [ ! -f "$local_secrets" ]; then
  bash "$root/deploy/overlays/local/make-secrets.sh" >/dev/null
  created_local=1
fi
bash "$root/deploy/render.sh" server "$work/server" sha-validate
bash "$root/deploy/render.sh" local "$work/local"
[ "$created_local" = 1 ] && rm -f "$local_secrets"

# 3. Schemas: built-in kinds from the default catalog, CRDs (cert-manager, Traefik, Flux) from the CRDs catalog.
#    The SOPS Secret is skipped: its "sops" field is not part of the Secret schema.
"$kubeconform" -strict -summary -skip Secret \
  -schema-location default \
  -schema-location 'https://raw.githubusercontent.com/datreeio/CRDs-catalog/main/{{.Group}}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json' \
  "$work/server/addons/addons.yaml" "$work/server/infra/infra.yaml" "$work/server/apps/apps.yaml" \
  "$work/local/local.yaml" "$root/deploy/flux/sync.yaml"

# 4. Workload basics, read from the rendered output.
"$python" - "$work/server/infra/infra.yaml" "$work/server/apps/apps.yaml" "$work/local/local.yaml" <<'PY'
import sys, yaml

problems = []
for path in sys.argv[1:]:
    with open(path, encoding="utf-8") as f:
        for doc in yaml.safe_load_all(f):
            if not doc or doc.get("kind") not in ("Deployment", "StatefulSet", "CronJob"):
                continue
            spec = doc["spec"]["jobTemplate"]["spec"] if doc["kind"] == "CronJob" else doc["spec"]
            name = f'{doc["kind"]}/{doc["metadata"]["name"]} ({path.rsplit("/", 1)[-1]})'
            for c in spec["template"]["spec"]["containers"]:
                image = c["image"]
                if image.endswith(":latest") or (":" not in image.rsplit("/", 1)[-1] and "@" not in image):
                    problems.append(f"{name}: image {image} has no fixed tag")
                if not c.get("resources", {}).get("limits", {}).get("memory"):
                    problems.append(f"{name}: container {c['name']} has no memory limit")
                if doc["kind"] != "CronJob" and doc["metadata"]["name"] != "stripe-listener" and "readinessProbe" not in c:
                    problems.append(f"{name}: container {c['name']} has no readiness probe")
for p in problems:
    print("FAIL:", p)
sys.exit(1 if problems else 0)
PY

echo "deploy/: both overlays render and validate"
