# ============================================
# EShop UI (Next.js) - multi-stage build
# Build context: ui/ (see docker-compose.override.selfhost.yml); ui/.dockerignore keeps node_modules and .next out.
# next.config.ts sets output: "standalone", so the runtime image carries the traced server and no node_modules.
# ============================================

# --------------------------------------------
# Dependencies: the npm ci layer is reused until package*.json changes
# --------------------------------------------
# Exact tag, so a rebuild is reproducible; the same Node major as local development.
FROM node:24.21.0-alpine3.23 AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

# --------------------------------------------
# Build
# --------------------------------------------
FROM node:24.21.0-alpine3.23 AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# Every page is rendered on demand, so the build needs no gateway.
RUN npm run build

# --------------------------------------------
# Runtime
# --------------------------------------------
FROM node:24.21.0-alpine3.23 AS final
WORKDIR /app

# HOSTNAME: the standalone server binds to it, and Docker sets it to the container id, which would make the server
# listen on that name's address only. API_BASE_URL is the gateway on the compose network; compose may override it.
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0 \
    API_BASE_URL=http://api-gateway:8080

# Owned by root, so the process cannot rewrite its own code. Only .next/cache (the image optimizer and fetch cache)
# must be writable by the app user.
COPY --from=build /app/public ./public
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
RUN mkdir -p .next/cache \
    && chown 1000:1000 .next/cache

# The base image's "node" user, numeric so a kubelet enforcing runAsNonRoot can verify it (hadolint DL3066).
USER 1000:1000

EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["node", "-e", "fetch('http://127.0.0.1:3000/api/health').then(r => process.exit(r.ok ? 0 : 1), () => process.exit(1))"]

CMD ["node", "server.js"]
