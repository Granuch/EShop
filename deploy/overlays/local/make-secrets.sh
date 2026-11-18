#!/usr/bin/env bash
# Writes deploy/overlays/local/secrets.env with fresh random credentials for a local or test cluster.
# Leaves an existing file alone (pass --force to replace it). The file is gitignored; never commit it.
set -euo pipefail

dir="$(cd "$(dirname "$0")" && pwd)"
out="$dir/secrets.env"

if [ -f "$out" ] && [ "${1:-}" != "--force" ]; then
  echo "$out exists; leaving it (use --force to replace)"
  exit 0
fi

# Hex only: the values go into connection strings, where ; = and quotes would need escaping.
rand() { od -An -N"$1" -tx1 /dev/urandom | tr -d ' \n'; }

umask 077
cat > "$out" <<EOF
identity-db-password=$(rand 24)
catalog-db-password=$(rand 24)
ordering-db-password=$(rand 24)
payment-db-password=$(rand 24)
notification-db-password=$(rand 24)
redis-password=$(rand 24)
rabbitmq-username=eshop
rabbitmq-password=$(rand 24)
jwt-secret-key=$(rand 32)
internal-service-api-key=$(rand 32)
seed-admin-password=Local-$(rand 8)-Admin1!
smtp-username=
smtp-password=
smtp-from-email=noreply@eshop.local
stripe-secret-key=
stripe-publishable-key=
stripe-webhook-secret=
seq-admin-password=$(rand 12)
grafana-admin-password=$(rand 12)
duckdns-token=
EOF
echo "wrote $out"
