#!/usr/bin/env bash
# Fails when .env.example carries a value that looks like a real secret.
#
# The rule is by key name, not by known leaked values: every key whose name says it holds a secret must be empty or
# hold a placeholder (CHANGE_ME / REPLACE_ME). The only exceptions are the well-known local defaults below, each with
# the reason it is safe to publish. A new secret key is therefore covered without editing this script.
#
# Usage: bash .github/scripts/check-env-example.sh [file]   (default .env.example)
set -euo pipefail

file=${1:-.env.example}

# KEY=VALUE pairs that may keep a literal default.
allowed=(
  # Identity seeds this admin only in Development and Sandbox (Program.cs); every other environment seeds roles only.
  "IDENTITY_SEED_ADMIN_PASSWORD=Admin123!"
  # RabbitMQ's own default user. docker-compose.override.production.yml requires RABBITMQ_USERNAME/PASSWORD via :?.
  "RABBITMQ_PASSWORD=guest"
)

secret_name='(PASSWORD|SECRET|API_KEY|_KEY)$'
failures=0
while IFS= read -r line || [[ -n $line ]]; do
  line=${line%$'\r'}
  [[ $line =~ ^([A-Z0-9_]+)=(.*)$ ]] || continue
  key=${BASH_REMATCH[1]}
  value=${BASH_REMATCH[2]}

  # Real Stripe credentials have a recognisable shape whatever key they sit under.
  if [[ $value =~ (sk|rk)_live_|whsec_[A-Za-z0-9]{16,}|(sk|pk)_test_[A-Za-z0-9]{16,} ]]; then
    echo "::error file=$file::$key looks like a real Stripe credential"
    failures=$((failures + 1))
    continue
  fi

  [[ $key =~ $secret_name ]] || continue
  [[ -z $value || $value == *CHANGE_ME* || $value == *REPLACE_ME* ]] && continue

  for pair in "${allowed[@]}"; do
    [[ $line == "$pair" ]] && continue 2
  done

  echo "::error file=$file::$key must be empty or a CHANGE_ME placeholder in a tracked template"
  failures=$((failures + 1))
done < "$file"

if (( failures > 0 )); then
  echo "$failures secret-like value(s) in $file"
  exit 1
fi
echo "$file: every secret-named key is empty, a placeholder or an allowed local default"
