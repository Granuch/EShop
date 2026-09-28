#!/usr/bin/env bash
# Dumps what a Docker Smoke run needs for a post-mortem: every eshop-* container's log, its state and restart count,
# and a memory/CPU snapshot (docker-ci DC-48: compose sets no limits, so this is where a starved runner would show).
# It only reads; judging the logs is scan_logs.py's job.
#
# Usage: bash .github/scripts/collect-logs.sh <output dir>
set -euo pipefail

out=${1:?usage: collect-logs.sh <output dir>}
mkdir -p "$out"

mapfile -t containers < <(docker ps -a --filter 'name=^eshop-' --format '{{.Names}}' | sort)
if (( ${#containers[@]} == 0 )); then
  echo "no eshop-* containers found" | tee "$out/containers.tsv"
  exit 0
fi

printf 'name\tstatus\thealth\trestarts\texit_code\n' > "$out/containers.tsv"
for c in "${containers[@]}"; do
  docker logs "$c" > "$out/$c.log" 2>&1 || true
  docker inspect -f "{{.Name}}	{{.State.Status}}	{{if .State.Health}}{{.State.Health.Status}}{{else}}-{{end}}	{{.RestartCount}}	{{.State.ExitCode}}" "$c" \
    | sed 's#^/##' >> "$out/containers.tsv"
done

docker stats --no-stream --format '{{.Name}}\t{{.MemUsage}}\t{{.CPUPerc}}' > "$out/stats.tsv" 2>&1 || true
echo "collected ${#containers[@]} containers into $out"
column -t -s $'\t' "$out/containers.tsv" 2>/dev/null || cat "$out/containers.tsv"
