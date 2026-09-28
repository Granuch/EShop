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
- Grafana: `http://localhost:3001` (`GRAFANA_PORT`)
- Jaeger: `http://localhost:16686`

> **Grafana is on 3001, not 3000**: 3000 is the `ui/` Next.js dev server's port and the default CORS origin. A `.env`
> created before this change may still set `GRAFANA_PORT=3000`; then both can be up at once, and on Windows
> `localhost:3000` and `127.0.0.1:3000` can resolve to different processes (`::1` vs IPv4), so confirm which one
> answered before debugging either.

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
- Verify the OTel collector is running (`monitoring` profile) and read its log.
- Check the OTLP endpoint configuration in the service environment (`OpenTelemetry__OtlpEndpoint`).
- Compose samples 10% of traces by default (`OTEL_SAMPLING_RATIO`); set `1.0` to see every request.
- Spans listed under **`OTLPResourceNoServiceName`** carry no resource: the exporter was not registered through the
  SDK's `AddOtlpExporter`, so it never received the service name. Services list under their own names
  (`EShop.Catalog.API`, …); the Docker Smoke workflow fails if any of the seven is missing.
- To tell an application-side problem from a collector-side one, POST a hand-made span with a `service.name`
  attribute to `http://localhost:4318/v1/traces`; if it appears named in Jaeger, the collector path is fine.
- After editing `infrastructure/otel-collector/otel-collector-config.yml`, run `docker compose restart otel-collector`:
  `up -d` does not recreate a container for a changed mounted file.

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
