# ============================================
# EShop Payment API - multi-stage build
# Build context: the repository root (see docker-compose.yml).
# Every service Dockerfile follows the same template; keep them in step.
# ============================================

# --------------------------------------------
# Build: restore, then publish
# --------------------------------------------
# Exact SDK version, so a rebuild is reproducible; Dependabot (docker ecosystem) proposes each new patch.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src

# Project files first, so the restore layer is reused until a .csproj changes. The list must hold every project
# the API references, directly or transitively; a missing one only prints "Skipping project" and silently
# costs the cache (docker-ci DC-06).
COPY ["src/Services/Payment/EShop.Payment.API/EShop.Payment.API.csproj", "src/Services/Payment/EShop.Payment.API/"]
COPY ["src/Services/Payment/EShop.Payment.Application/EShop.Payment.Application.csproj", "src/Services/Payment/EShop.Payment.Application/"]
COPY ["src/Services/Payment/EShop.Payment.Domain/EShop.Payment.Domain.csproj", "src/Services/Payment/EShop.Payment.Domain/"]
COPY ["src/Services/Payment/EShop.Payment.Infrastructure/EShop.Payment.Infrastructure.csproj", "src/Services/Payment/EShop.Payment.Infrastructure/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Domain/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Application/EShop.BuildingBlocks.Application.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Application/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/EShop.BuildingBlocks.Infrastructure.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Messaging/EShop.BuildingBlocks.Messaging.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Messaging/"]
RUN dotnet restore "src/Services/Payment/EShop.Payment.API/EShop.Payment.API.csproj"

COPY . .
WORKDIR "/src/src/Services/Payment/EShop.Payment.API"
# One compile: publish builds the project itself, so there is no separate "dotnet build" step (DC-07).
RUN dotnet publish "EShop.Payment.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# --------------------------------------------
# Runtime
# --------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS final

# curl serves the HEALTHCHECK below and the compose healthchecks.
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Non-root user. UID/GID 999 are pinned because the existing *-logs volumes hold files owned by 999.
RUN groupadd --system --gid 999 appuser \
    && useradd --system --uid 999 --gid appuser appuser

WORKDIR /app
# The application stays owned by root, so the process cannot rewrite its own binaries; only logs/ and data/ are
# writable. Chowning /app after the COPY would store every published file twice (DC-33).
COPY --from=build /app/publish .
RUN mkdir -p /app/logs /app/data \
    && chown appuser:appuser /app/logs /app/data

# Numeric, so a kubelet enforcing runAsNonRoot can verify the user is not root (hadolint DL3066).
USER 999:999

EXPOSE 8080

# Same shape as the compose healthchecks, so a plain "docker run" reports health the same way.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD ["curl", "-fsS", "http://localhost:8080/health/ready"]

# Defaults; docker-compose.yml overrides the environment.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

ENTRYPOINT ["dotnet", "EShop.Payment.API.dll"]
