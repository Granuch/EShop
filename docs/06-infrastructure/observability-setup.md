# Observability Setup

Practical setup guide for local observability components.

---

## Components

When monitoring profile is enabled, the local stack includes:

- Seq (logs)
- Prometheus (metrics store/scraper)
- Grafana (dashboards)
- Jaeger (trace UI)
- OpenTelemetry Collector
- Exporters (for selected infrastructure components)

---

## Start Local Observability

From repository root:

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

---

## Default Access URLs

(Values can be overridden by `.env`)

- Seq: `http://localhost:5341`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`
- Jaeger: `http://localhost:16686`

> **Port 3000 clash**: Grafana's default port is also the Next.js dev-server default used by
> `ui/`. On Windows, `localhost:3000` and `127.0.0.1:3000` can resolve to two different running
> processes at once (`::1` vs IPv4), so if both are up, confirm which one actually answered before
> debugging "Grafana looks wrong" or "the frontend dev server isn't loading."

---

## Service Endpoints to Validate

For gateway and services, verify:
- readiness/liveness endpoints
- `/prometheus`
- `/metrics`

---

## Basic Validation Checklist

1. Containers are healthy in `docker compose ps`.
2. Prometheus targets show as up.
3. Logs are arriving in Seq.
4. Dashboards show active metrics in Grafana.
5. Traces appear in Jaeger after requests.

---

## Troubleshooting

### No metrics in Prometheus
- Check service endpoint exposure and scrape target config.
- Confirm service container/network availability.

### No logs in Seq
- Check Serilog sink configuration for service.
- Confirm Seq container is running and reachable.

### No traces in Jaeger
- Verify OTEL collector is running.
- Check OTLP endpoint configuration in service environment.

---

## Operational Notes

- Keep local credentials convenient for development only.
- For non-local environments, use secure credentials and strict access control.

---

## Related Documents

- [Observability](observability.md)
- [Resilience](resilience.md)
- [Services](../05-services/)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
