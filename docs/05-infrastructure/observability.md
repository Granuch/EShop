# 🔍 Observability

Comprehensive logging, metrics, and distributed tracing для мікросервісної архітектури.

---

## Three Pillars of Observability

1. **Logs** - What happened? (Seq)
2. **Metrics** - How much? (Prometheus + Grafana)
3. **Traces** - Where did it go? (Jaeger / OpenTelemetry)

```
          ┌────────────────────────────────┐
          │      Application Code          │
          └────────────┬───────────────────┘
                       │
          ┌────────────┼───────────────────┐
          │            │                   │
      ┌───▼────┐  ┌───▼────┐      ┌──────▼─────┐
      │  Logs  │  │ Metrics│      │   Traces   │
      │ (Seq)  │  │(Prom.) │      │  (Jaeger)  │
      └────────┘  └───┬────┘      └────────────┘
                      │
                ┌─────▼──────┐
                │  Grafana   │
                │ Dashboards │
                └────────────┘
```

---

## Logging with Serilog & Seq

### Setup Serilog

**NuGet Packages:**
```xml
<ItemGroup>
  <PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
  <PackageReference Include="Serilog.Sinks.Seq" Version="6.0.0" />
  <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
  <PackageReference Include="Serilog.Enrichers.Environment" Version="2.3.0" />
  <PackageReference Include="Serilog.Enrichers.Thread" Version="3.1.0" />
</ItemGroup>
```

---

### Program.cs Configuration

```csharp
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProperty("Application", "EShop.Catalog.API")
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.Seq(builder.Configuration.GetValue<string>("Seq:Url") ?? "http://localhost:5341")
    .CreateLogger();

builder.Host.UseSerilog();

try
{
    Log.Information("Starting Catalog API");
    
    var app = builder.Build();
    
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"]);
            
            if (httpContext.User.Identity?.IsAuthenticated == true)
            {
                diagnosticContext.Set("UserId", httpContext.User.FindFirst("sub")?.Value);
            }
        };
    });
    
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
```

---

### appsettings.json

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "Microsoft.EntityFrameworkCore": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://localhost:5341",
          "apiKey": null
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  },
  
  "Seq": {
    "Url": "http://localhost:5341"
  }
}
```

---

### Structured Logging Examples

```csharp
// ✅ Good - Structured logging
_logger.LogInformation(
    "Product {ProductId} created by {UserId} with price {Price}",
    productId, userId, price);

// ✅ With context
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["OrderId"] = orderId,
    ["UserId"] = userId
}))
{
    _logger.LogInformation("Processing order");
    _logger.LogInformation("Order validated");
    _logger.LogInformation("Order saved");
}

// ✅ Exception logging
try
{
    await _repository.SaveAsync(product);
}
catch (Exception ex)
{
    _logger.LogError(ex, 
        "Failed to save product {ProductId}", 
        product.Id);
    throw;
}

// ❌ Bad - String interpolation
_logger.LogInformation($"Product {productId} created"); // Don't do this!
```

---

### Seq Docker Setup

```yaml
# docker-compose.yml

services:
  seq:
    image: datalust/seq:latest
    container_name: eshop-seq
    environment:
      ACCEPT_EULA: Y
      SEQ_API_KEY: ${SEQ_API_KEY:-}
    ports:
      - "5341:80"
    volumes:
      - seq_data:/data
    networks:
      - eshop-network

volumes:
  seq_data:
```

**Access Seq UI**: http://localhost:5341

---

## Metrics with Prometheus

### Setup OpenTelemetry Metrics

**NuGet Packages:**
```xml
<ItemGroup>
  <PackageReference Include="OpenTelemetry" Version="1.6.0" />
  <PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.6.0-rc.1" />
  <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.6.0" />
  <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.5.1-beta.1" />
  <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.5.1-beta.1" />
</ItemGroup>
```

---

### Program.cs Configuration

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddPrometheusExporter();
    });

var app = builder.Build();

// Expose /metrics endpoint for Prometheus
app.MapPrometheusScrapingEndpoint();

app.Run();
```

---

### Custom Metrics

```csharp
public class ProductService
{
    private static readonly Counter<int> ProductsCreated = Meter.CreateCounter<int>(
        "products_created_total",
        description: "Total number of products created");

    private static readonly Histogram<double> ProductPrices = Meter.CreateHistogram<double>(
        "product_price_distribution",
        unit: "USD",
        description: "Distribution of product prices");

    private static readonly Meter Meter = new("EShop.Catalog");

    public async Task<Product> CreateProductAsync(CreateProductCommand command)
    {
        var product = Product.Create(...);
        await _repository.AddAsync(product);

        // Record metrics
        ProductsCreated.Add(1, 
            new KeyValuePair<string, object?>("category", product.CategoryId));
        
        ProductPrices.Record(product.Price.Amount);

        return product;
    }
}
```

---

### Prometheus Docker Setup

```yaml
# docker-compose.yml

services:
  prometheus:
    image: prom/prometheus:latest
    container_name: eshop-prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus_data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
      - '--storage.tsdb.retention.time=30d'
    networks:
      - eshop-network

volumes:
  prometheus_data:
```

---

### prometheus.yml

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'catalog-api'
    static_configs:
      - targets: ['catalog-api:80']
    metrics_path: '/metrics'
    scrape_interval: 10s

  - job_name: 'basket-api'
    static_configs:
      - targets: ['basket-api:80']

  - job_name: 'ordering-api'
    static_configs:
      - targets: ['ordering-api:80']

  - job_name: 'api-gateway'
    static_configs:
      - targets: ['api-gateway:80']
```

---

## Grafana Dashboards

### Setup

```yaml
# docker-compose.yml

services:
  grafana:
    image: grafana/grafana:latest
    container_name: eshop-grafana
    ports:
      - "3001:3000"
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: admin
      GF_INSTALL_PLUGINS: grafana-piechart-panel
    volumes:
      - grafana_data:/var/lib/grafana
      - ./grafana/provisioning:/etc/grafana/provisioning
      - ./grafana/dashboards:/var/lib/grafana/dashboards
    depends_on:
      - prometheus
    networks:
      - eshop-network

volumes:
  grafana_data:
```

**Access Grafana**: http://localhost:3001 (admin/admin)

---

### Sample Dashboard

**grafana/dashboards/catalog-api.json:**

```json
{
  "dashboard": {
    "title": "Catalog API Metrics",
    "panels": [
      {
        "title": "Request Rate",
        "targets": [
          {
            "expr": "rate(http_requests_total{job=\"catalog-api\"}[5m])"
          }
        ],
        "type": "graph"
      },
      {
        "title": "Response Time P95",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, http_request_duration_seconds_bucket{job=\"catalog-api\"})"
          }
        ],
        "type": "graph"
      },
      {
        "title": "Error Rate",
        "targets": [
          {
            "expr": "rate(http_requests_total{job=\"catalog-api\",status=~\"5..\"}[5m])"
          }
        ],
        "type": "graph"
      },
      {
        "title": "Products Created",
        "targets": [
          {
            "expr": "rate(products_created_total[5m])"
          }
        ],
        "type": "stat"
      }
    ]
  }
}
```

---

## Distributed Tracing with OpenTelemetry + Jaeger

### Setup

**NuGet Packages:**
```xml
<ItemGroup>
  <PackageReference Include="OpenTelemetry" Version="1.6.0" />
  <PackageReference Include="OpenTelemetry.Exporter.Jaeger" Version="1.5.1" />
  <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.5.1-beta.1" />
  <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.5.1-beta.1" />
  <PackageReference Include="OpenTelemetry.Instrumentation.SqlClient" Version="1.5.1-beta.1" />
</ItemGroup>
```

---

### Program.cs Configuration

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService("catalog-api")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                    ["service.version"] = "1.0.0"
                }))
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = (httpContext) =>
                {
                    // Don't trace health checks
                    return !httpContext.Request.Path.StartsWithSegments("/health");
                };
            })
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForText = true;
            })
            .AddSource("MassTransit")
            .AddJaegerExporter(options =>
            {
                options.AgentHost = builder.Configuration.GetValue<string>("Jaeger:Host") ?? "localhost";
                options.AgentPort = builder.Configuration.GetValue<int>("Jaeger:Port", 6831);
            });
    });
```

---

### Custom Spans

```csharp
public class ProductService
{
    private static readonly ActivitySource ActivitySource = new("EShop.Catalog");

    public async Task<Product> GetProductByIdAsync(Guid id)
    {
        using var activity = ActivitySource.StartActivity("GetProductById");
        activity?.SetTag("product.id", id);

        try
        {
            var product = await _repository.GetByIdAsync(id);
            
            if (product is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Product not found");
                return null;
            }

            activity?.SetTag("product.name", product.Name);
            activity?.SetTag("product.price", product.Price.Amount);

            return product;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }
}
```

---

### Jaeger Docker Setup

```yaml
# docker-compose.yml

services:
  jaeger:
    image: jaegertracing/all-in-one:latest
    container_name: eshop-jaeger
    ports:
      - "5775:5775/udp"
      - "6831:6831/udp"
      - "6832:6832/udp"
      - "5778:5778"
      - "16686:16686"  # UI
      - "14268:14268"
      - "14250:14250"
      - "9411:9411"
    environment:
      COLLECTOR_ZIPKIN_HOST_PORT: :9411
    networks:
      - eshop-network
```

**Access Jaeger UI**: http://localhost:16686

---

## Health Checks

### Setup

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgres",
        tags: new[] { "db", "ready" })
    .AddRedis(
        builder.Configuration.GetValue<string>("Redis:ConnectionString")!,
        name: "redis",
        tags: new[] { "cache", "ready" })
    .AddRabbitMQ(
        rabbitConnectionString: builder.Configuration.GetValue<string>("RabbitMQ:ConnectionString")!,
        name: "rabbitmq",
        tags: new[] { "messaging", "ready" })
    .AddUrlGroup(
        new Uri("http://localhost:5101/health"),
        name: "identity-service",
        tags: new[] { "services" });

var app = builder.Build();

// Health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // Always healthy
});
```

---

### Custom Health Check

```csharp
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly CatalogDbContext _context;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.Database.CanConnectAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Database is unreachable",
                exception: ex);
        }
    }
}

// Register
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "db" });
```

---

## Correlation IDs

Track requests across services:

```csharp
// Middleware to add correlation ID

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        context.Response.Headers.Add(CorrelationIdHeader, correlationId);

        // Add to log context
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

// Register
app.UseMiddleware<CorrelationIdMiddleware>();
```

---

## Alerts

### Prometheus Alerting Rules

```yaml
# alerts.yml

groups:
  - name: catalog_api
    interval: 30s
    rules:
      - alert: HighErrorRate
        expr: rate(http_requests_total{status=~"5.."}[5m]) > 0.05
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "High error rate on {{ $labels.job }}"
          description: "Error rate is {{ $value }} per second"

      - alert: SlowResponseTime
        expr: histogram_quantile(0.95, http_request_duration_seconds_bucket) > 1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Slow response time on {{ $labels.job }}"
          description: "P95 latency is {{ $value }} seconds"
```

---

## Best Practices

### ✅ DO

1. **Use Structured Logging** - With Serilog message templates
2. **Add Context** - UserId, OrderId, CorrelationId
3. **Set Appropriate Log Levels** - Info/Warning/Error
4. **Monitor Key Metrics** - Request rate, error rate, latency
5. **Use Distributed Tracing** - For cross-service debugging
6. **Implement Health Checks** - For orchestrators (Kubernetes)
7. **Set Up Alerts** - For critical issues

### ❌ DON'T

1. **Don't Log Sensitive Data** - Passwords, tokens, credit cards
2. **Don't Over-Log** - Avoid Debug logs in production
3. **Don't Block on Logging** - Use async
4. **Don't Ignore Correlation** - Always pass correlation ID
5. **Don't Skip Metrics** - Measure everything important

---

## Наступні кроки

- ✅ [Resilience Patterns](resilience.md)
- ✅ [Production Readiness](../../10-production-readiness/)
- ✅ [Monitoring & Alerts](../../10-production-readiness/monitoring-alerts.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
