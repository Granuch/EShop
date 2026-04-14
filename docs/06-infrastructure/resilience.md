# 🛡️ Resilience

Підвищення стійкості мікросервісів через retry, circuit breaker, timeout, та fallback patterns.

---

## Огляд

Resilience забезпечує:
- ✅ **Retry Logic** - Автоматичний повтор при тимчасових збоях
- ✅ **Circuit Breaker** - Запобігання каскадним збоям
- ✅ **Timeout** - Обмеження часу очікування
- ✅ **Fallback** - Резервна поведінка при помилках
- ✅ **Bulkhead Isolation** - Ізоляція ресурсів
- ✅ **Rate Limiting** - Захист від перенавантаження

---

## Polly Library

**Polly** - найпопулярніша бібліотека для resilience patterns у .NET.

### NuGet Packages

```xml
<ItemGroup>
  <PackageReference Include="Polly" Version="8.2.0" />
  <PackageReference Include="Polly.Extensions.Http" Version="3.0.0" />
  <PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.0.0" />
</ItemGroup>
```

---

## 1️⃣ Retry Pattern

Автоматичний повтор при тимчасових збоях (network glitches, transient errors).

### Basic Retry

```csharp
// Simple retry 3 times
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .RetryAsync(3);

await retryPolicy.ExecuteAsync(async () =>
{
    await _httpClient.GetAsync("https://api.example.com/products");
});
```

---

### Retry with Exponential Backoff

```csharp
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .Or<TimeoutException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 2^1, 2^2, 2^3
        onRetry: (exception, timeSpan, retryCount, context) =>
        {
            _logger.LogWarning(
                "Retry {RetryCount} after {Delay}s due to {Exception}",
                retryCount, timeSpan.TotalSeconds, exception.GetType().Name);
        });

await retryPolicy.ExecuteAsync(async () =>
{
    return await _repository.GetProductAsync(productId);
});
```

---

### Retry with Jitter (Recommended)

Додає випадкову затримку, щоб уникнути "thundering herd":

```csharp
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: (retryAttempt, context) =>
        {
            var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
            return baseDelay + jitter;
        },
        onRetry: (exception, timeSpan, retryCount, context) =>
        {
            _logger.LogWarning("Retry {RetryCount} after {Delay}s", 
                retryCount, timeSpan.TotalSeconds);
        });
```

---

## 2️⃣ Circuit Breaker Pattern

Запобігає каскадним збоям, "розмикаючи" коло після N невдалих спроб.

```
┌──────────────────────────────────────────────────────┐
│                 Circuit Breaker States                │
├──────────────────────────────────────────────────────┤
│                                                       │
│   ┌────────┐    Threshold      ┌──────────┐         │
│   │ Closed │─────exceeded──────►│   Open   │         │
│   └────┬───┘                    └─────┬────┘         │
│        │                              │               │
│        │                              │ Timeout       │
│        │                              │               │
│        │       Success       ┌────────▼────┐         │
│        └────────────────────►│ Half-Open   │         │
│                              └─────────────┘         │
│                                                       │
└──────────────────────────────────────────────────────┘
```

### Implementation

```csharp
var circuitBreakerPolicy = Policy
    .Handle<HttpRequestException>()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 3,  // Open after 3 failures
        durationOfBreak: TimeSpan.FromSeconds(30), // Stay open for 30s
        onBreak: (exception, duration) =>
        {
            _logger.LogWarning(
                "Circuit breaker opened for {Duration}s due to {Exception}",
                duration.TotalSeconds, exception.GetType().Name);
        },
        onReset: () =>
        {
            _logger.LogInformation("Circuit breaker reset");
        },
        onHalfOpen: () =>
        {
            _logger.LogInformation("Circuit breaker half-open, testing...");
        });

try
{
    await circuitBreakerPolicy.ExecuteAsync(async () =>
    {
        return await _httpClient.GetAsync("https://unstable-api.com");
    });
}
catch (BrokenCircuitException)
{
    _logger.LogWarning("Circuit is open, request blocked");
    // Return cached data or default response
}
```

---

## 3️⃣ Timeout Pattern

Обмеження часу очікування запиту.

```csharp
var timeoutPolicy = Policy
    .TimeoutAsync<HttpResponseMessage>(
        timeout: TimeSpan.FromSeconds(10),
        onTimeoutAsync: (context, timespan, task) =>
        {
            _logger.LogWarning("Request timed out after {Timeout}s", timespan.TotalSeconds);
            return Task.CompletedTask;
        });

try
{
    var response = await timeoutPolicy.ExecuteAsync(async ct =>
    {
        return await _httpClient.GetAsync("https://slow-api.com", ct);
    }, CancellationToken.None);
}
catch (TimeoutRejectedException)
{
    _logger.LogError("Request timeout exceeded");
}
```

---

## 4️⃣ Fallback Pattern

Резервна поведінка при помилках.

```csharp
var fallbackPolicy = Policy<Product>
    .Handle<HttpRequestException>()
    .Or<TimeoutException>()
    .FallbackAsync(
        fallbackValue: Product.Default, // Return default product
        onFallbackAsync: (result, context) =>
        {
            _logger.LogWarning("Fallback triggered for product");
            return Task.CompletedTask;
        });

var product = await fallbackPolicy.ExecuteAsync(async () =>
{
    return await _productService.GetProductAsync(productId);
});
```

**Fallback with Cache:**

```csharp
var fallbackPolicy = Policy<Product>
    .Handle<Exception>()
    .FallbackAsync(async (ct) =>
    {
        _logger.LogWarning("Falling back to cached data");
        return await _cache.GetProductAsync(productId, ct);
    });
```

---

## 5️⃣ Policy Wrap (Combining Policies)

Комбінування кількох policies (RECOMMENDED).

```csharp
// Order matters: Fallback > CircuitBreaker > Retry > Timeout

var timeoutPolicy = Policy
    .TimeoutAsync<HttpResponseMessage>(10);

var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

var circuitBreakerPolicy = Policy
    .Handle<HttpRequestException>()
    .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

var fallbackPolicy = Policy<HttpResponseMessage>
    .Handle<Exception>()
    .FallbackAsync(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"error\":\"Service unavailable\",\"cached\":true}")
    });

// Combine policies
var resiliencePolicy = Policy.WrapAsync(
    fallbackPolicy,
    circuitBreakerPolicy,
    retryPolicy,
    timeoutPolicy
);

var response = await resiliencePolicy.ExecuteAsync(async () =>
{
    return await _httpClient.GetAsync("https://api.example.com/products");
});
```

---

## 6️⃣ Bulkhead Isolation

Обмеження кількості паралельних запитів для ізоляції ресурсів.

```csharp
var bulkheadPolicy = Policy
    .BulkheadAsync<HttpResponseMessage>(
        maxParallelization: 10,  // Max 10 concurrent requests
        maxQueuingActions: 20,   // Max 20 queued requests
        onBulkheadRejectedAsync: context =>
        {
            _logger.LogWarning("Bulkhead capacity exceeded, request rejected");
            return Task.CompletedTask;
        });

try
{
    var response = await bulkheadPolicy.ExecuteAsync(async () =>
    {
        return await _httpClient.GetAsync("https://api.example.com");
    });
}
catch (BulkheadRejectedException)
{
    _logger.LogWarning("Too many concurrent requests");
}
```

---

## HttpClient with Polly

### Typed HttpClient with Policies

```csharp
// Product API Client
public interface IProductApiClient
{
    Task<Product?> GetProductAsync(Guid id);
}

public class ProductApiClient : IProductApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductApiClient> _logger;

    public ProductApiClient(HttpClient httpClient, ILogger<ProductApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Product?> GetProductAsync(Guid id)
    {
        var response = await _httpClient.GetAsync($"/api/v1/products/{id}");
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<Product>();
    }
}
```

---

### Register with Polly Policies

```csharp
// Program.cs

builder.Services.AddHttpClient<IProductApiClient, ProductApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("ProductApi:Url")!);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy())
.AddPolicyHandler(GetTimeoutPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError() // 5xx and 408
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.NotFound)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                var logger = context.GetLogger();
                logger.LogWarning(
                    "Retry {RetryAttempt} after {Delay}s due to {StatusCode}",
                    retryAttempt, timespan.TotalSeconds, outcome.Result?.StatusCode);
            });
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, duration) =>
            {
                Console.WriteLine($"Circuit opened for {duration.TotalSeconds}s");
            },
            onReset: () => Console.WriteLine("Circuit reset"));
}

static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
{
    return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10));
}
```

---

## Rate Limiting

### ASP.NET Core Rate Limiter (New in .NET 7)

```csharp
// Program.cs

builder.Services.AddRateLimiter(options =>
{
    // Fixed Window
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 10;
    });

    // Sliding Window
    options.AddSlidingWindowLimiter("sliding", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 6; // 10s segments
    });

    // Token Bucket
    options.AddTokenBucketLimiter("token", opt =>
    {
        opt.TokenLimit = 100;
        opt.TokensPerPeriod = 20;
        opt.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
    });

    // Concurrency Limiter
    options.AddConcurrencyLimiter("concurrency", opt =>
    {
        opt.PermitLimit = 50;
        opt.QueueLimit = 10;
    });

    // Global rate limiter
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.FindFirst("sub")?.Value ?? "anonymous";
        
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1)
        });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseRateLimiter();

// Apply to endpoints
app.MapGet("/api/v1/products", GetProducts)
    .RequireRateLimiting("fixed");

app.MapPost("/api/v1/orders", CreateOrder)
    .RequireRateLimiting("token");
```

---

### Custom Rate Limiter Middleware

```csharp
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDistributedCache _cache;
    private readonly int _maxRequests = 100;
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"rate_limit:{userId}";

        var currentCount = await GetRequestCountAsync(key);

        if (currentCount >= _maxRequests)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded",
                retryAfter = _window.TotalSeconds
            });
            return;
        }

        await IncrementRequestCountAsync(key);
        await _next(context);
    }

    private async Task<int> GetRequestCountAsync(string key)
    {
        var value = await _cache.GetStringAsync(key);
        return int.TryParse(value, out var count) ? count : 0;
    }

    private async Task IncrementRequestCountAsync(string key)
    {
        var count = await GetRequestCountAsync(key) + 1;
        await _cache.SetStringAsync(key, count.ToString(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _window
        });
    }
}
```

---

## Database Resilience

### EF Core Retry on Failure

```csharp
builder.Services.AddDbContext<CatalogDbContext>(options =>
{
    options.UseNpgsql(
        connectionString,
        npgsqlOptions =>
        {
            // Automatic retry on transient failures
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null);

            npgsqlOptions.CommandTimeout(30);
        });
});
```

---

### Manual Retry for Queries

```csharp
var retryPolicy = Policy
    .Handle<NpgsqlException>()
    .Or<TimeoutException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
        onRetry: (exception, timeSpan, retryCount, context) =>
        {
            _logger.LogWarning("Database retry {RetryCount} after {Delay}s", 
                retryCount, timeSpan.TotalSeconds);
        });

var products = await retryPolicy.ExecuteAsync(async () =>
{
    return await _context.Products
        .Where(p => p.CategoryId == categoryId)
        .ToListAsync();
});
```

---

## Message Broker Resilience

### RabbitMQ with MassTransit

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");

        cfg.ReceiveEndpoint("order-service-queue", e =>
        {
            e.ConfigureConsumer<OrderCreatedConsumer>(context);

            // Retry policy
            e.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(5),
                    intervalDelta: TimeSpan.FromSeconds(2));
                
                // Don't retry validation errors
                r.Ignore<ValidationException>();
            });

            // Circuit breaker
            e.UseCircuitBreaker(cb =>
            {
                cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                cb.TripThreshold = 15;        // Open after 15 failures
                cb.ActiveThreshold = 10;      // Reset after 10 successes
                cb.ResetInterval = TimeSpan.FromMinutes(5);
            });

            // Rate limit
            e.UseRateLimit(1000, TimeSpan.FromSeconds(1));
        });
    });
});
```

---

## Health Checks with Resilience

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString,
        healthQuery: "SELECT 1",
        name: "postgres",
        timeout: TimeSpan.FromSeconds(3),
        tags: new[] { "db", "ready" })
    .AddRedis(
        redisConnectionString,
        name: "redis",
        timeout: TimeSpan.FromSeconds(3),
        tags: new[] { "cache", "ready" })
    .AddRabbitMQ(
        rabbitConnectionString,
        name: "rabbitmq",
        timeout: TimeSpan.FromSeconds(3),
        tags: new[] { "messaging", "ready" });

// Readiness probe
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

// Liveness probe
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // Always healthy
});
```

---

## Chaos Engineering

### Simmy (Chaos Engineering for Polly)

```xml
<PackageReference Include="Polly.Contrib.Simmy" Version="0.4.0" />
```

```csharp
// Inject faults for testing

var chaosPolicy = MonkeyPolicy.InjectException(with =>
    with.Fault(new HttpRequestException("Simulated failure"))
        .InjectionRate(0.1)  // 10% of requests fail
        .Enabled(builder.Environment.IsDevelopment())
);

builder.Services.AddHttpClient<IProductApiClient, ProductApiClient>()
    .AddPolicyHandler(chaosPolicy)  // Test resilience
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());
```

---

## Best Practices

### ✅ DO

1. **Combine Policies** - Retry + Circuit Breaker + Timeout
2. **Use Exponential Backoff** - With jitter
3. **Log Retries** - For observability
4. **Set Timeouts** - On all external calls
5. **Implement Fallbacks** - Return cached/default data
6. **Test Failure Scenarios** - Use chaos engineering
7. **Monitor Circuit Breaker State** - Track open/close events

### ❌ DON'T

1. **Don't Retry Forever** - Set retry limits
2. **Don't Retry on Non-Transient Errors** - 400, 401, 403, 404
3. **Don't Ignore Timeout** - Always set reasonable timeouts
4. **Don't Cascade Retries** - Avoid retry storms
5. **Don't Forget Bulkhead** - Isolate critical resources

---

## Monitoring Resilience

### Metrics to Track

```csharp
private static readonly Counter<int> RetryCounter = Meter.CreateCounter<int>(
    "polly_retries_total",
    description: "Total number of retries");

private static readonly Counter<int> CircuitBreakerOpenCounter = Meter.CreateCounter<int>(
    "polly_circuit_breaker_opened_total",
    description: "Number of times circuit breaker opened");

var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        3,
        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
        onRetry: (exception, timeSpan, retryCount, context) =>
        {
            RetryCounter.Add(1, 
                new KeyValuePair<string, object?>("service", "product-api"));
        });
```

---

## Resilience Checklist

### External HTTP Calls
- [ ] Retry with exponential backoff + jitter
- [ ] Circuit breaker (5 failures, 30s break)
- [ ] Timeout (10s)
- [ ] Fallback (cache or default)
- [ ] Bulkhead (limit concurrency)

### Database Calls
- [ ] Connection retry on failure
- [ ] Query timeout (30s)
- [ ] Connection pooling configured

### Message Queue
- [ ] Consumer retry (3-5 attempts)
- [ ] Dead letter queue
- [ ] Circuit breaker
- [ ] Idempotent consumers

### API Gateway
- [ ] Rate limiting (per user/IP)
- [ ] Global timeout
- [ ] Request size limits

---

## Наступні кроки

- ✅ [Observability](observability.md) - Monitor failures
- ✅ [Testing Strategy](../../08-testing/testing-strategy.md) - Test resilience
- ✅ [Production Readiness](../../10-production-readiness/)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
