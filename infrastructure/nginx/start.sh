#!/bin/sh
# Entrypoint of the self-hosting nginx (docker-compose.override.selfhost.yml).
#
# nginx refuses to start when a configured certificate file is missing, and the first certificate can only be issued
# once port 80 answers the ACME challenge. So the HTTPS server is rendered only when the certificate exists, and until
# then nginx serves the challenge plus a 503. The loop re-renders and reloads every hour: it picks up the first
# certificate without a restart, and every renewal certbot writes (nginx reads certificates only at start or reload).
set -eu

: "${SELFHOST_DOMAIN:?SELFHOST_DOMAIN is required}"
cert="/etc/letsencrypt/live/${SELFHOST_DOMAIN}/fullchain.pem"
conf=/etc/nginx/conf.d/default.conf

render() {
    if [ -f "$cert" ]; then
        envsubst '${SELFHOST_DOMAIN}' < /opt/eshop/nginx/https.conf.template > "$conf.new"
    else
        echo "start.sh: no certificate at $cert yet; serving HTTP (ACME challenge + 503) only" >&2
        envsubst '${SELFHOST_DOMAIN}' < /opt/eshop/nginx/http-only.conf.template > "$conf.new"
    fi
    mv "$conf.new" "$conf"
}

render

(
    while :; do
        sleep 3600
        render
        nginx -t -q && nginx -s reload
    done
) &

exec nginx -g 'daemon off;'
