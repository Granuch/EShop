#!/usr/bin/env bash
# Builds the compose env file CI uses, from .env.example, so the template is the single source of every variable.
#
# Every value that still holds a CHANGE_ME placeholder gets a fresh random one: the services' startup guards refuse
# placeholders outside Development, and a run that brings the stack up must use the same file that compose-validate
# renders. Every other line keeps the template's value. The generated values are throwaway and never leave the runner.
#
# Usage: bash .github/scripts/ci-env.sh <output file>
set -euo pipefail

out=${1:?usage: ci-env.sh <output file>}
template="$(cd "$(dirname "$0")/../.." && pwd)/.env.example"

: > "$out"
while IFS= read -r line || [[ -n $line ]]; do
  line=${line%$'\r'}
  if [[ $line =~ ^([A-Z0-9_]+)=.*CHANGE_ME ]]; then
    # 48 hex characters: longer than JwtSecretGuard's 32, and free of every placeholder pattern.
    printf '%s=ci_%s\n' "${BASH_REMATCH[1]}" "$(openssl rand -hex 24)" >> "$out"
  else
    printf '%s\n' "$line" >> "$out"
  fi
done < "$template"

echo "wrote $out ($(grep -c '=ci_' "$out") generated secrets)"
