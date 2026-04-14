# 🚪 API Gateway

Єдина точка входу для всіх клієнтських запитів з підтримкою YARP, auth, rate limiting та resilience.

---

## Огляд

API Gateway відповідає за:
- ✅ Reverse Proxy для всіх мікросервісів (YARP)
- ✅ JWT Token Validation
- ✅ Rate Limiting (per user/IP)
- ✅ CORS Configuration
- ✅ Request/Response Transformation
- ✅ Circuit Breaker (Polly)
- ✅ Health Checks aggregation
- ✅ API Versioning
- ✅ Request logging та tracing

---

## Архітектура

### Технології

| Компонент | Технологія | Призначення |
|-----------|------------|-------------|
| **Framework** | ASP.NET Core 9.0 | Web API |
| **Reverse Proxy** | YARP (Yet Another Reverse Proxy) | Request forwarding |
| **Auth** | JWT Bearer | Token validation |
| **Resilience** | Polly | Circuit breaker, retry |
| **Rate Limiting** | ASP.NET Core Rate Limiting | Throttling |
| **Observability** | OpenTelemetry | Distributed tracing |

---

## Routes Configuration

### appsettings.json

```json
{
  "ReverseProxy": {
    "Routes": {
      "identity-route": {
        "ClusterId": "identity-cluster",
        "Match": {
          "Path": "/api/v1/auth/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "/api/v1/auth/{**catch-all}"
          }
        ]
      },
      "catalog-route": {
        "ClusterId": "catalog-cluster",
        "Match": {
          "Path": "/api/v1/products/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "/api/v1/products/{**catch-all}"
          }
        ],
        "RateLimiterPolicy": "fixed"
      },
      "basket-route": {
        "ClusterId": "basket-cluster",
        "AuthorizationPolicy": "authenticated",
        "Match": {
          "Path": "/api/v1/basket/{**catch-all}"
        }
      },
      "orders-route": {
        "ClusterId": "orders-cluster",
        "AuthorizationPolicy": "authenticated",
        "Match": {
          "Path": "/api/v1/orders/{**catch-all}"
        }
      }
    },
    
    "Clusters": {
      "identity-cluster": {
        "Destinations": {
          "identity-service": {
            "Address": "http://localhost:5101"
          }
        },
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Path": "/health"
          }
        }
      },
      "catalog-cluster": {
        "Destinations": {
          "catalog-service": {
            "Address": "http://localhost:5102"
          }
        },
        "LoadBalancingPolicy": "RoundRobin"
      },
      "basket-cluster": {
        "Destinations": {
          "basket-service": {
            "Address": "http://localhost:5103"
          }
        }
      },
      "orders-cluster": {
        "Destinations": {
          "orders-service": {
            "Address": "http://localhost:5104"
          }
        }
      }
    }
  }
}
```

---

## JWT Authentication

```csharp
// Program.cs

var builder = WebApplication.CreateBuilder(args);

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };
        
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                context.Response.Headers.Add("Token-Expired", "true");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", policy => 
        policy.RequireAuthenticatedUser());
    
    options.AddPolicy("admin", policy => 
        policy.RequireRole("Admin"));
});
```

---

## Rate Limiting

```csharp
// Program.cs

builder.Services.AddRateLimiter(options =>
{
    // Fixed window limiter
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10;
    });
    
    // Sliding window limiter
    options.AddSlidingWindowLimiter("sliding", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 4;
    });
    
    // Token bucket (more flexible)
    options.AddTokenBucketLimiter("token", opt =>
    {
        opt.TokenLimit = 100;
        opt.ReplenishmentPeriod = TimeSpan.FromMinutes(1);
        opt.TokensPerPeriod = 50;
    });
    
    // Per-user rate limiting
    options.AddPolicy("per-user", context =>
    {
        var userId = context.User?.FindFirst("sub")?.Value ?? "anonymous";
        
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});
```

---

## CORS Configuration

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",  // React dev server
                "https://eshop.com"       // Production
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors("AllowFrontend");
```

---

## Resilience (Polly)

```csharp
// Polly Circuit Breaker

builder.Services.AddHttpClient("catalog-service")
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                // Log retry
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
                // Log circuit opened
            },
            onReset: () =>
            {
                // Log circuit closed
            });
}
```

---

## Health Checks

```csharp
// Program.cs

builder.Services.AddHealthChecks()
    .AddUrlGroup(new Uri("http://localhost:5101/health"), "identity-service")
    .AddUrlGroup(new Uri("http://localhost:5102/health"), "catalog-service")
    .AddUrlGroup(new Uri("http://localhost:5103/health"), "basket-service")
    .AddUrlGroup(new Uri("http://localhost:5104/health"), "orders-service");

var app = builder.Build();

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
    Predicate = _ => false,
    ResponseWriter = (context, _) =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync("{\"status\": \"Healthy\"}");
    }
});
```

---

## Request Transformation

```csharp
// Add custom headers

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        // Add X-Forwarded-For header
        builderContext.AddRequestTransform(async context =>
        {
            context.ProxyRequest.Headers.Add(
                "X-Forwarded-For", 
                context.HttpContext.Connection.RemoteIpAddress?.ToString());
        });
        
        // Add trace ID
        builderContext.AddRequestTransform(async context =>
        {
            var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            context.ProxyRequest.Headers.Add("X-Trace-Id", traceId);
        });
    });
```

---

## Complete Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Authentication & Authorization
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", policy => 
        policy.RequireAuthenticatedUser());
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins("http://localhost:3000", "https://eshop.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddUrlGroup(new Uri("http://localhost:5101/health"), "identity")
    .AddUrlGroup(new Uri("http://localhost:5102/health"), "catalog")
    .AddUrlGroup(new Uri("http://localhost:5103/health"), "basket")
    .AddUrlGroup(new Uri("http://localhost:5104/health"), "orders");

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddJaegerExporter();
    });

var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapReverseProxy();
app.MapHealthChecks("/health");

app.Run();
```

---

## Request Flow Example

```
Client Request:
GET https://api.eshop.com/api/v1/products

↓ API Gateway (YARP)

1. CORS Check ✓
2. Rate Limiting ✓
3. JWT Validation ✓ (if required)
4. Add trace headers ✓
5. Route to Catalog Service

↓ Catalog Service
GET http://localhost:5102/api/v1/products

← Response

↓ API Gateway
← Response to Client
```

---

## Security Best Practices

### ✅ Implemented

1. **JWT Validation** - All protected routes verify tokens
2. **HTTPS Only** - In production
3. **CORS** - Whitelist allowed origins
4. **Rate Limiting** - Prevent DDoS
5. **Header Sanitization** - Remove sensitive headers
6. **Request Size Limits** - Max 10MB

### Configuration

```json
{
  "JwtSettings": {
    "SecretKey": "env:JWT_SECRET",
    "Issuer": "EShop.Identity",
    "Audience": "EShop.Clients"
  },
  
  "RateLimiting": {
    "Enabled": true,
    "PermitLimit": 100,
    "WindowMinutes": 1
  },
  
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "https://eshop.com"
    ]
  },
  
  "RequestLimits": {
    "MaxBodySize": 10485760
  }
}
```

---

## Monitoring

### Key Metrics

- Request count per route
- Response time (P50, P95, P99)
- Error rate (4xx, 5xx)
- Active connections
- Circuit breaker state

### Grafana Dashboard

```json
{
  "dashboard": {
    "title": "API Gateway Metrics",
    "panels": [
      {
        "title": "Requests per Second",
        "targets": [
          {
            "expr": "rate(http_requests_total[5m])"
          }
        ]
      },
      {
        "title": "Response Time P95",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, http_request_duration_seconds_bucket)"
          }
        ]
      }
    ]
  }
}
```

---

## Load Balancing

YARP підтримує різні стратегії:

```json
{
  "Clusters": {
    "catalog-cluster": {
      "LoadBalancingPolicy": "RoundRobin",
      "Destinations": {
        "catalog-1": {
          "Address": "http://catalog-service-1:5102"
        },
        "catalog-2": {
          "Address": "http://catalog-service-2:5102"
        }
      }
    }
  }
}
```

**Supported policies:**
- `RoundRobin` - Default
- `LeastRequests` - Route to least busy instance
- `Random` - Random selection
- `PowerOfTwoChoices` - Pick best of 2 random

---

## Testing

### Integration Test

```csharp
[Fact]
public async Task GET_Products_ShouldReturn200()
{
    // Arrange
    var client = _factory.CreateClient();
    
    // Act
    var response = await client.GetAsync("/api/v1/products");
    
    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
}

[Fact]
public async Task GET_Basket_WithoutAuth_ShouldReturn401()
{
    // Arrange
    var client = _factory.CreateClient();
    
    // Act
    var response = await client.GetAsync("/api/v1/basket/123");
    
    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
```

---

## Наступні кроки

- ✅ [Infrastructure Overview](../../06-infrastructure/)
- ✅ [Resilience Patterns](../../06-infrastructure/resilience.md)
- ✅ [Observability Setup](../../06-infrastructure/observability.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
