# ============================================
# Multi-stage Dockerfile for EShop Basket API
# Optimized for .NET 10 production deployment
# ============================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Services/Basket/EShop.Basket.API/EShop.Basket.API.csproj", "src/Services/Basket/EShop.Basket.API/"]
COPY ["src/Services/Basket/EShop.Basket.Application/EShop.Basket.Application.csproj", "src/Services/Basket/EShop.Basket.Application/"]
COPY ["src/Services/Basket/EShop.Basket.Infrastructure/EShop.Basket.Infrastructure.csproj", "src/Services/Basket/EShop.Basket.Infrastructure/"]
COPY ["src/Services/Basket/EShop.Basket.Domain/EShop.Basket.Domain.csproj", "src/Services/Basket/EShop.Basket.Domain/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Application/EShop.BuildingBlocks.Application.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Application/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Domain/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/EShop.BuildingBlocks.Infrastructure.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/"]
COPY ["src/BuildingBlocks/EShop.BuildingBlocks.Messaging/EShop.BuildingBlocks.Messaging.csproj", "src/BuildingBlocks/EShop.BuildingBlocks.Messaging/"]

RUN dotnet restore "src/Services/Basket/EShop.Basket.API/EShop.Basket.API.csproj"

COPY . .
WORKDIR "/src/src/Services/Basket/EShop.Basket.API"
RUN dotnet build "EShop.Basket.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EShop.Basket.API.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
RUN groupadd -r appuser && useradd -r -g appuser appuser

WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p /app/logs /app/data && chown -R appuser:appuser /app

USER appuser

EXPOSE 8080 8081

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health/ready || exit 1

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

ENTRYPOINT ["dotnet", "EShop.Basket.API.dll"]
