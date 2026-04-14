# Observability

Observability in EShop is based on logs, metrics, and traces.

---

## Three Pillars

1. **Logs** — structured event records (Serilog + Seq)
2. **Metrics** — quantitative runtime signals (Prometheus + Grafana)
3. **Traces** — end-to-end request/message flow visibility (OpenTelemetry + Jaeger)

---

## Current Stack

| Capability | Tooling | Notes |
|------------|---------|-------|
| Structured logging | Serilog | Service-level structured logs |
| Log aggregation | Seq | Centralized log query UI |
| Metrics collection | Prometheus | Scrapes service/exporter endpoints |
| Metrics visualization | Grafana | Dashboards and alert views |
| Tracing | OpenTelemetry + Collector + Jaeger | Distributed tracing pipeline |

---

## Runtime Endpoints

Typical endpoints exposed by services/gateway:
- Health: `/health`, `/health/ready`, `/health/live` (varies by service)
- Prometheus: `/prometheus`
- OpenTelemetry metrics: `/metrics`

---

## OpenTelemetry Integration

Shared OpenTelemetry setup is centralized in infrastructure extensions and includes:
- ASP.NET Core instrumentation
- HttpClient instrumentation
- EF Core instrumentation
- runtime metrics instrumentation
- custom sources/meters per service

Export path uses OTLP to collector where enabled.

---

## Logging Practices

- Use structured logging templates with named properties.
- Include correlation context where available.
- Keep sensitive data out of logs.
- Use level discipline (`Information`, `Warning`, `Error`, etc.).

---

## Operational Use

### During incidents

1. Start from gateway/service health endpoints.
2. Correlate failures in logs (Seq).
3. Check latency/error trends in Prometheus/Grafana.
4. Use traces to isolate cross-service bottlenecks.

### During performance tuning

- Validate cache/database/message latency with telemetry
- Compare baseline metrics before/after changes

---

## Local Development Notes

Observability components can be started via compose monitoring profile.
Use local convenient credentials in `.env` for development only.

---

## Related Documents

- [Observability Setup](observability-setup.md)
- [Resilience](resilience.md)
- [Message Broker](message-broker.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
