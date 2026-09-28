#!/usr/bin/env bash
# Renders every compose profile combination the repo supports and fails on an error OR a warning.
#
# `docker compose config` renders only the services in the selected profiles, so a bare `config` (what CI used to run)
# never saw an app service or the production override (docker-ci DC-03). A warning fails the run too: an unset
# variable renders as an empty string and would otherwise pass silently.
#
# Usage: bash .github/scripts/compose-validate.sh <env file>   (build one with ci-env.sh)
set -euo pipefail

env_file=${1:?usage: compose-validate.sh <env file>}
cd "$(dirname "$0")/../.."

base=(-f docker-compose.yml)
production=(-f docker-compose.yml -f docker-compose.override.production.yml)
public=(-f docker-compose.yml -f docker-compose.override.public.yml)

failures=0
check() {
  local name=$1; shift
  local err services
  err=$(mktemp)
  if ! services=$(docker compose --env-file "$env_file" "$@" config --services 2> "$err") \
    || ! docker compose --env-file "$env_file" "$@" config -q 2>> "$err" \
    || [[ -s $err ]]; then
    echo "::error::compose render '$name' failed or warned:"
    cat "$err"
    failures=$((failures + 1))
  else
    printf '%-20s ok, %2d services\n' "$name" "$(wc -l <<< "$services")"
  fi
  rm -f "$err"
}

check "infra only"         "${base[@]}"
check "sandbox"            "${base[@]}" --profile sandbox
check "sandbox+monitoring" "${base[@]}" --profile sandbox --profile monitoring
check "sandbox+stripe"     "${base[@]}" --profile sandbox --profile stripe
check "development"        "${base[@]}" --profile development
check "production"         "${production[@]}" --profile production --profile monitoring
check "public"             "${public[@]}" --profile sandbox --profile monitoring

if (( failures > 0 )); then
  echo "$failures compose render(s) failed"
  exit 1
fi
