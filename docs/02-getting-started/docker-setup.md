# Docker Setup Guide
 
This guide describes Docker usage for the current EShop backend stack.
 
---
 
## Prerequisites
 
- Docker Desktop (or Docker Engine + Compose plugin)
- `.env` file created from `.env.example`
- At least 8 GB RAM assigned to Docker (recommended)
---
 
## Compose Files in Repository Root
 
The project uses root-level compose files:
 
- `docker-compose.yml` — main stack definition
- `docker-compose.override.production.yml` — production-oriented overrides
- `docker-compose.override.public.yml` — public/runtime override scenario
- `.env.example` — environment template
---
 
## Environment Setup
 
Create local env file:
 
```bash
cp .env.example .env
```
 
PowerShell:
 
```powershell
Copy-Item .env.example .env
```
 
Adjust values in `.env` before startup (JWT keys, database passwords, service ports, optional Stripe/Mail settings).
 
For local development, convenient password values are acceptable. Replace them with secure secret stores outside local environments.
 
---
 
## Start Commands
 
### Sandbox profile (recommended for local development)
 
```bash
docker compose --profile sandbox up -d
```
 
### Sandbox + monitoring profile
 
```bash
docker compose --profile sandbox --profile monitoring up -d
```
 
### Rebuild and start
 
```bash
docker compose --profile sandbox up -d --build
```
 
### Stop
 
```bash
docker compose down
```
 
### Stop and remove volumes
 
```bash
docker compose down -v
```
 
---
 
## What Runs in the Stack
 
Core runtime includes:
 
- API Gateway (YARP)
- Identity, Catalog, Basket, Ordering, Payment, Notification APIs
- PostgreSQL instances per service context
- Redis
- RabbitMQ
- Mailpit
Monitoring profile adds:
 
- Seq
- Prometheus
- Grafana
- Jaeger
- OTEL Collector
- Exporters
---
 
## Operational Commands
 
```bash
# List containers
docker compose ps
 
# Follow all logs
docker compose logs -f
 
# Follow one service
docker compose logs -f api-gateway
 
# Restart one service
docker compose restart api-gateway
 
# Execute command in a container
docker compose exec rabbitmq rabbitmq-diagnostics -q check_running
```
 
---
 
## Health and Access
 
Common local URLs (default values from `.env.example`):
 
- API Gateway: `http://localhost:7000`
- RabbitMQ UI: `http://localhost:15672`
- Seq: `http://localhost:5341`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`
- Jaeger: `http://localhost:16686`
- Mailpit UI: `http://localhost:8025`
### API Documentation (Scalar / OpenAPI)
 
Each service exposes its own Scalar UI and OpenAPI document directly (not proxied through the gateway). This is available in every environment except Production (`EShopApiDocs.IsExposedIn`), so it works under Sandbox, Development, and Testing.
 
**Requires the service's `ports:` mapping to be present in `docker-compose.yml`** — by default only `api-gateway` and `payment-api` are exposed on the host. To expose the others (Identity, Catalog, Ordering, Basket), add a `ports:` entry to each service block, mapping to the ports already defined in `.env.example`:
 
- Identity API: `http://localhost:7001/scalar/v1`
- Catalog API: `http://localhost:7004/scalar/v1`
- Ordering API: `http://localhost:7005/scalar/v1`
- Basket API: `http://localhost:7006/scalar/v1`
- Payment API: `http://localhost:7008/openapi/v1.json` (OpenAPI document only — no Scalar UI wired up in `Program.cs`)
The API Gateway serves its own OpenAPI document (not Scalar) at `/openapi/v1.json`, but does not proxy `/scalar` for any downstream service — YARP only forwards the business routes defined under `ReverseProxy:Routes` in `appsettings.json`.
 
> **Production:** these ports must be reset to `!reset []` in `docker-compose.override.production.yml` for any service where a host `ports:` mapping was added, so Scalar/OpenAPI is never reachable from outside the cluster in Production.
 
Gateway health endpoints:
 
- `GET /health`
- `GET /health/ready`
- `GET /health/live`
---
 
## Troubleshooting
 
### Containers not starting
 
```bash
docker compose logs <service-name>
```
 
### Port conflicts
 
- Check mapped ports in `.env`
- Change the conflicting port value and restart stack
### Dependency not healthy
 
```bash
docker compose ps
docker compose restart <service-name>
```
 
### Clean reset
 
```bash
docker compose down -v
docker compose --profile sandbox up -d
```
 
---
 
## Related Documents
 
- [Prerequisites](prerequisites.md)
- [Local Setup](local-setup.md)
- [Infrastructure](../06-infrastructure/)
---
 
**Version**: 2.1  
**Last Updated**: 2026-09-15