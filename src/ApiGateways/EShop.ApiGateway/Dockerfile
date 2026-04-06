# ============================================
# Multi-stage Dockerfile for EShop API Gateway
# Optimized for .NET 10 production deployment
# ============================================

# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for restore
COPY ["src/ApiGateways/EShop.ApiGateway/EShop.ApiGateway.csproj", "src/ApiGateways/EShop.ApiGateway/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/EShop.BuildingBlocks.Infrastructure.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Application/EShop.BuildingBlocks.Application.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Application/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Domain/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Messaging/EShop.BuildingBlocks.Messaging.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Messaging/"]

# Restore dependencies
RUN dotnet restore "src/ApiGateways/EShop.ApiGateway/EShop.ApiGateway.csproj"

# Copy full source
COPY . .

# Build
WORKDIR "/src/src/ApiGateways/EShop.ApiGateway"
RUN dotnet build "EShop.ApiGateway.csproj" -c Release -o /app/build

# ============================================
# Stage 2: Publish
# ============================================
FROM build AS publish
RUN dotnet publish "EShop.ApiGateway.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ============================================
# Stage 3: Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN groupadd -r appuser && useradd -r -g appuser appuser

WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Create logs directory with proper permissions
RUN mkdir -p /app/logs && \
    chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose ports
EXPOSE 8080 8081

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health/ready || exit 1

# Environment defaults (override in docker-compose)
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

# Entry point
ENTRYPOINT ["dotnet", "EShop.ApiGateway.dll"]
