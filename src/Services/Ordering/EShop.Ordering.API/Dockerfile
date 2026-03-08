# ============================================
# Multi-stage Dockerfile for EShop Ordering API
# Optimized for .NET 10 production deployment
# ============================================

# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution file and project files for restore
COPY ["src/Services/Ordering/EShop.Ordering.API/EShop.Ordering.API.csproj", "src/Services/Ordering/EShop.Ordering.API/"]
COPY ["src/Services/Ordering/EShop.Ordering.Application/EShop.Ordering.Application.csproj", "src/Services/Ordering/EShop.Ordering.Application/"]
COPY ["src/Services/Ordering/EShop.Ordering.Infrastructure/EShop.Ordering.Infrastructure.csproj", "src/Services/Ordering/EShop.Ordering.Infrastructure/"]
COPY ["src/Services/Ordering/EShop.Ordering.Domain/EShop.Ordering.Domain.csproj", "src/Services/Ordering/EShop.Ordering.Domain/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Application/EShop.BuildingBlocks.Application.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Application/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Domain/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/EShop.BuildingBlocks.Infrastructure.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Messaging/EShop.BuildingBlocks.Messaging.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Messaging/"]

# Restore dependencies
RUN dotnet restore "src/Services/Ordering/EShop.Ordering.API/EShop.Ordering.API.csproj"

# Copy all source code
COPY . .

# Build the application
WORKDIR "/src/src/Services/Ordering/EShop.Ordering.API"
RUN dotnet build "EShop.Ordering.API.csproj" -c Release -o /app/build

# ============================================
# Stage 2: Publish
# ============================================
FROM build AS publish
RUN dotnet publish "EShop.Ordering.API.csproj" \
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

# Create directories for logs and data with proper permissions
RUN mkdir -p /app/logs /app/data && \
    chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose ports
EXPOSE 8080 8081

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health/ready || exit 1

# Environment variables (overridden by docker-compose)
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

# Entry point
ENTRYPOINT ["dotnet", "EShop.Ordering.API.dll"]
