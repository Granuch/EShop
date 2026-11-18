#!/usr/bin/env bash
# Renders a deployment overlay into plain manifests.
#
#   deploy/render.sh server <out-dir> [<image-tag>]   ->  <out-dir>/infra/{infra.yaml,secrets.enc.yaml}
#                                                         <out-dir>/apps/apps.yaml
#   deploy/render.sh local  <out-dir> [<image-tag>]   ->  <out-dir>/local.yaml
#
# <image-tag> replaces the "master" tag of the eshop images (CI passes sha-<commit>). The server output is
# what CI publishes as the Flux artifact: Flux applies infra/ first, then apps/. The SOPS-encrypted Secret is
# copied byte for byte, never passed through kustomize, so its MAC still matches when Flux decrypts it.
#
# Uses $KUSTOMIZE if set, else `kustomize`, else `kubectl kustomize`.
set -euo pipefail

overlay="${1:?usage: render.sh <server|local> <out-dir> [image-tag]}"
out="${2:?usage: render.sh <server|local> <out-dir> [image-tag]}"
tag="${3:-}"
root="$(cd "$(dirname "$0")/.." && pwd)"

if [ -n "${KUSTOMIZE:-}" ]; then
  build() { $KUSTOMIZE build --load-restrictor LoadRestrictionsNone "$1"; }
elif command -v kustomize >/dev/null 2>&1; then
  build() { kustomize build --load-restrictor LoadRestrictionsNone "$1"; }
else
  build() { kubectl kustomize --load-restrictor LoadRestrictionsNone "$1"; }
fi

retag() {
  if [ -n "$tag" ]; then
    sed -E "s#(image: ghcr\.io/granuch/eshop-[a-z-]+):master\$#\1:${tag}#"
  else
    cat
  fi
}

case "$overlay" in
  server)
    mkdir -p "$out/infra" "$out/apps"
    build "$root/deploy/overlays/server/infra" | retag > "$out/infra/infra.yaml"
    cp "$root/deploy/overlays/server/infra/secrets.enc.yaml" "$out/infra/secrets.enc.yaml"
    build "$root/deploy/overlays/server/apps" | retag > "$out/apps/apps.yaml"
    ;;
  local)
    if [ ! -f "$root/deploy/overlays/local/secrets.env" ]; then
      echo "deploy/overlays/local/secrets.env is missing: run deploy/overlays/local/make-secrets.sh first" >&2
      exit 1
    fi
    mkdir -p "$out"
    build "$root/deploy/overlays/local" | retag > "$out/local.yaml"
    ;;
  *)
    echo "unknown overlay: $overlay (server or local)" >&2
    exit 2
    ;;
esac

if [ -n "$tag" ] && grep -rqE 'image: ghcr\.io/granuch/eshop-[a-z-]+:master$' "$out"; then
  echo "an eshop image kept the master tag after retagging" >&2
  exit 1
fi
