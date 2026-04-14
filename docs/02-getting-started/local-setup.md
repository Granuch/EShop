# Local Setup

This guide explains how to run the current backend solution locally.

---

## Prerequisites

Complete the requirements from [prerequisites.md](prerequisites.md):

- .NET 10 SDK
- Docker + Docker Compose
- Git
- IDE (Visual Studio / Rider / VS Code)

---

## Step 1: Clone the Repository

```bash
git clone https://github.com/Granuch/EShop.git
cd EShop
```

---

## Step 2: Prepare Environment Variables

Create local environment file from template:

```bash
cp .env.example .env
```

On Windows PowerShell:

```powershell
Copy-Item .env.example .env
```

For local development, convenient password values in `.env` are acceptable. Use secure secret storage for non-local environments.

---

## Step 3: Start the Stack with Docker Compose

From repository root:

```bash
docker compose --profile sandbox up -d
```

If you also need observability tools (Prometheus, Grafana, Jaeger, OTEL pipeline):

```bash
docker compose --profile sandbox --profile monitoring up -d
```

Check status:

```bash
docker compose ps
```

Stop:

```bash
docker compose down
```

Stop and remove volumes:

```bash
docker compose down -v
```

---

## Step 4: Verify Key Endpoints

Default endpoints are configured via `.env`.

- API Gateway: `http://localhost:7000`
- RabbitMQ UI: `http://localhost:15672`
- Seq: `http://localhost:5341`
- Prometheus: `http://localhost:9090` (monitoring profile)
- Grafana: `http://localhost:3000` (monitoring profile)
- Jaeger: `http://localhost:16686` (monitoring profile)
- Mailpit UI: `http://localhost:8025`

Health checks:

```bash
curl http://localhost:7000/health
curl http://localhost:7000/health/ready
curl http://localhost:7000/health/live
```

---

## Step 5: Run Services from IDE (Optional)

If you prefer local process debugging instead of full Docker runtime:

1. Open `EShop.slnx`.
2. Set multiple startup projects.
3. Start required APIs (for example identity, catalog, basket, ordering, payment, notification, gateway).

When running mixed mode (some services local, some in Docker), verify connection strings and hostnames for dependencies.

---

## Step 6: Run from CLI (Optional)

You can start individual services from repository root, one per terminal.

```bash
dotnet run --project src/Services/Identity/EShop.Identity.API/EShop.Identity.API.csproj
dotnet run --project src/Services/Catalog/EShop.Catalog.API/EShop.Catalog.API.csproj
dotnet run --project src/Services/Basket/EShop.Basket.API/EShop.Basket.API.csproj
dotnet run --project src/Services/Ordering/EShop.Ordering.API/EShop.Ordering.API.csproj
dotnet run --project src/Services/Payment/EShop.Payment.API/EShop.Payment.API.csproj
dotnet run --project src/Services/Notification/EShop.Notification.API/EShop.Notification.API.csproj
dotnet run --project src/ApiGateways/EShop.ApiGateway/EShop.ApiGateway.csproj
```

---

## Troubleshooting

### Port already in use

```bash
docker compose ps
docker compose logs <service-name>
```

Then free the conflicting port or change the mapped port in `.env`.

### Containers are unhealthy

```bash
docker compose logs -f <service-name>
docker compose restart <service-name>
```

### Build/runtime config issues

```bash
dotnet restore
dotnet build
```

---

## Useful Commands

```bash
# Build solution
dotnet build

# Run all tests
dotnet test

# Follow logs
docker compose logs -f

# Follow one service
docker compose logs -f api-gateway
```

---

## Related Documents

- [Prerequisites](prerequisites.md)
- [Docker Setup](docker-setup.md)
- [Team Agreement](team-agreement.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
