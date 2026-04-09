# YARP Gateway — Implementation Plan

## Executive Summary

В репозитории уже есть заготовка `src/ApiGateways/EShop.ApiGateway`, но она пока содержит TODO и не интегрирована в общую production-архитектуру сервисов (`Program.cs`, строки 11–114). Существующие сервисы построены в едином стиле: **Layered DDD + CQRS (MediatR) + Clean boundaries (API/Application/Infrastructure/Domain)**, со сквозными практиками Serilog, OpenTelemetry, HealthChecks, JWT, MassTransit+RabbitMQ и Outbox.

Ниже — план внедрения нового **simulation-first YARP Gateway** с полноценным модулем email-уведомлений, выровненный по паттернам текущего решения.

---

## 1. Анализ существующих микросервисов

### 1.1 Технологический стек (сводная таблица)

| Область | Фактическое состояние в репозитории |
|---|---|
| .NET | Все проекты `net10.0` (см. `_tmp_projects.json`) |
| API фреймворк | ASP.NET Core Web API / Minimal API |
| ORM | EF Core 10.0.2 + Npgsql 10.0.0, InMemory в тестах |
| Messaging | MassTransit 8.4.1/8.5.2 + RabbitMQ |
| Outbox | Реализован в BuildingBlocks (`OutboxMessage`, `OutboxProcessorService`, `IntegrationEventOutbox`) |
| Logging | Serilog.AspNetCore 10.0.0 + Console/File/Seq |
| Validation | FluentValidation 12.1.1 + MediatR pipeline behavior |
| Mapping | Mapster (Catalog), ручной mapping в остальных |
| Auth | JWT Bearer (HS256), Identity service как issuer, internal API key для service-to-service |
| Health | AspNetCore.HealthChecks.* + custom checks |
| Tracing/Metrics | OpenTelemetry 1.15.0 + OTLP exporter + Prometheus endpoint |
| Caching | Redis + fallback in-memory; caching behaviors в pipeline |
| HTTP client | Typed HttpClient (например Notification -> Identity) |
| Tests | NUnit 4.5.1 + Moq + WebApplicationFactory + Testcontainers (Identity) |

#### Ключевые версии пакетов (по `_tmp_packages_summary.json`)

- `Yarp.ReverseProxy` `2.3.0` (уже подключен в `EShop.ApiGateway.csproj`)
- `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.2`
- `Serilog.AspNetCore` `10.0.0`
- `Serilog.Sinks.Seq` `9.0.0`
- `OpenTelemetry.Extensions.Hosting` `1.15.0`
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` `1.15.0`
- `AspNetCore.HealthChecks.NpgSql` `9.0.0`
- `AspNetCore.HealthChecks.Redis` `9.0.0`
- `MailKit` `4.15.1` (Notification)
- `MassTransit` `8.5.2`, `MassTransit.RabbitMQ` `8.4.1/8.5.2`

### 1.2 Архитектурные паттерны

#### Структура решения и проектов

**Source (29 проектов):**
- `src/ApiGateways/EShop.ApiGateway/EShop.ApiGateway.csproj` (Web API)
- `src/BuildingBlocks/*` (4 Class Library)
- Сервисы Basket/Catalog/Identity/Notification/Ordering/Payment: для каждого `API`, `Application`, `Domain`, `Infrastructure`.

**Tests (12 проектов):**
- Для каждого сервиса отдельные `UnitTests` и `IntegrationTests`.

#### Паттерн архитектуры

- На уровне solution: **modular monorepo с service-per-bounded-context**.
- На уровне сервиса: **слои API/Application/Domain/Infrastructure** (близко к Clean Architecture).
- На уровне use-case: **CQRS + MediatR**.
- На уровне endpoint-стиля:
  - Identity: Controllers (`AccountController`).
  - Остальные сервисы: Minimal API (`Map...Endpoints`).

#### CQRS / MediatR / Pipeline

В `...Application/Extensions/ServiceCollectionExtensions.cs` регистрируются:
- `AddMediatR(...)`
- `AddValidatorsFromAssembly(...)`
- Pipeline behaviors (`ValidationBehavior`, `LoggingBehavior`, иногда `TransactionBehavior`).

В Infra слое дополнительно подключаются:
- `CachingBehavior`
- `CacheInvalidationBehavior`

#### Result pattern / ошибки

- Общий `Result`/`Result<T>` в `src/BuildingBlocks/EShop.BuildingBlocks.Application/Result.cs`.
- Endpoints обрабатывают `result.Match(...)` и возвращают `Results.Problem(...)`.
- Глобальные exception middleware в API (например Catalog/Identity/Basket) отдают RFC7807-подобный `application/problem+json`.

#### Domain/Integration events, Outbox

- Aggregate roots поддерживают DomainEvents (`AggregateRoot<TId>`).
- Outbox реализован централизованно:
  - `OutboxMessage`
  - `IntegrationEventOutbox`
  - `OutboxProcessorService` с retry/dead-letter/type-resolution/correlation.
- MassTransit extension задает retry, circuit-breaker, delayed redelivery.

### 1.3 Соглашения о коде

- В проектах: `Nullable=enable`, `ImplicitUsings=enable`.
- Широко используются `record` для DTO/request-response.
- Интерфейсы с префиксом `I`.
- Нет обнаруженных `Directory.Build.props`, `Directory.Build.targets`, `.editorconfig`, `.ruleset`.
- Нейминг: PascalCase в публичном API, camelCase в JSON по умолчанию.
- Глобальные using-файлы не найдены.

### 1.4 Инфраструктура

- `docker-compose.yml` содержит полный стек: postgres (по сервисам), redis, rabbitmq, mailpit, seq, jaeger, prometheus, grafana, otel-collector.
- Dockerfile у API сервисов однотипные multi-stage (`sdk:10.0` -> `aspnet:10.0`, non-root, `curl` healthcheck).
- Конфиги: `appsettings.json` + `appsettings.{Environment}.json` + опциональный `appsettings.{Environment}.Local.json`.
- В `Notification` подтверждена локальная стратегия секретов (`appsettings.Development.Local.json` + комментарий про local-only credentials).

---

## 2. Архитектура YARP Gateway

**Рекомендация:** развивать существующий `src/ApiGateways/EShop.ApiGateway` (не отдельный новый solution), чтобы сохранить монорепо-подход и pipeline/tooling.

Логическая архитектура:
1. **Routing/Proxy слой** (YARP routes/clusters/transforms).
2. **Simulation слой** (перехват запроса, управление задержкой/ошибкой/шаблоном ответа).
3. **Notification слой** (асинхронные email-триггеры).
4. **Observability слой** (Serilog + OTel + metrics).
5. **Security слой** (JWT validation + authz policy + token forwarding).

---

## 3. Структура проекта

Целевая структура (внутри существующего проекта `src/ApiGateways/EShop.ApiGateway`):

- `Configuration/`
  - `GatewayOptions.cs`
  - `SimulationOptions.cs`
  - `EmailOptions.cs`
  - `RateLimitingOptions.cs`
- `Middleware/`
  - `CorrelationIdMiddleware.cs`
  - `SimulationDecisionMiddleware.cs`
  - `EmailTriggerMiddleware.cs`
- `Routing/`
  - `YarpConfigValidator.cs`
  - `TokenForwardingTransformProvider.cs`
- `Simulation/`
  - `ISimulationProfileProvider.cs`
  - `SimulationProfileProvider.cs`
  - `ISimulationResponseFactory.cs`
  - `SimulationResponseFactory.cs`
  - `SimulationProfile.cs`
- `Notifications/`
  - `IEmailNotificationService.cs`
  - `EmailNotificationService.cs`
  - `IEmailTemplateEngine.cs`
  - `TemplateEngine.cs`
  - `IAccountEmailResolver.cs`
  - `IdentityAccountEmailResolver.cs`
  - `GatewayEmailQueue.cs` (Channel-based background dispatcher)
- `Health/`
  - `DownstreamHealthCheck.cs`
  - `SmtpGatewayHealthCheck.cs`
- `Telemetry/`
  - `GatewayTelemetry.cs` (Meters, counters, histograms)
  - `GatewayActivitySource.cs`
- `Templates/` (email html шаблоны)
- `Program.cs`
- `appsettings*.json` с секциями `ReverseProxy`, `Simulation`, `Email`, `Gateway`, `OpenTelemetry`.

Тесты:
- `tests/ApiGateways/EShop.ApiGateway.UnitTests/`
  - `Simulation/`
  - `Notifications/`
  - `Routing/`
- `tests/ApiGateways/EShop.ApiGateway.IntegrationTests/`
  - `Fixtures/`
  - `Routes/`
  - `Simulation/`
  - `Notifications/`

---

## 4. Зависимости

### Рекомендуемые NuGet для Gateway

| Пакет | Версия | Почему |
|---|---:|---|
| `Yarp.ReverseProxy` | `2.3.0` | Уже используется в репо, основной proxy engine |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | `10.0.2` | Совместимость с остальными API |
| `Serilog.AspNetCore` | `10.0.0` | Единый structured logging стиль |
| `Serilog.Sinks.Console` | `6.1.1` | Локальная/контейнерная диагностика |
| `Serilog.Sinks.File` | `7.0.0` | Локальные rolling-логи как в сервисах |
| `Serilog.Sinks.Seq` | `9.0.0` | Централизованный сбор в Seq |
| `OpenTelemetry.Extensions.Hosting` | `1.15.0` | Общий OTel bootstrap |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `1.15.0` | Отправка в otel-collector |
| `OpenTelemetry.Instrumentation.AspNetCore` | `1.15.0` | HTTP spans/metrics |
| `OpenTelemetry.Instrumentation.Http` | `1.15.0` | Downstream вызовы |
| `OpenTelemetry.Instrumentation.Runtime` | `1.15.0` | runtime metrics |
| `prometheus-net.AspNetCore` | `8.2.1` | Метрики /prometheus как в сервисах |
| `AspNetCore.HealthChecks.UI.Client` | `9.0.0` | Единый format response writer |
| `MailKit` | `4.15.1` | Уже принят в Notification |
| `Microsoft.Extensions.Http.Polly` | `10.0.0` | Retry/circuit policies для resolver/email/downstream probing |
| `Polly.Extensions.Http` | `3.0.0` | Policy builder для HttpClient |

> Выравнивание email-стека: в кодовой базе уже production-путь через **MailKit** (`EmailService` в Notification). Для консистентности Gateway должен использовать тот же подход.

---

## 5. Конфигурация YARP

### Базовая конфигурация (`appsettings.json`)

- Секция `ReverseProxy:Routes` и `ReverseProxy:Clusters`.
- Для каждого сервиса отдельный cluster (identity/catalog/basket/ordering/payment/notification).
- Для simulation добавить route metadata, не отдельный физический cluster:
  - `Metadata:SimulationProfile=orders-default`
  - `Metadata:CriticalOperation=true`

Пример (сокращенно):

```json
{
  "ReverseProxy": {
    "Routes": {
      "orders-route": {
        "ClusterId": "ordering-cluster",
        "Match": { "Path": "/api/v1/orders/{**catch-all}" },
        "Transforms": [
          { "RequestHeader": "X-Correlation-ID", "Set": "{TraceIdentifier}" },
          { "RequestHeaderOriginalHost": "true" }
        ],
        "Metadata": {
          "SimulationProfile": "orders-default",
          "EmailOnFailure": "true"
        }
      }
    },
    "Clusters": {
      "ordering-cluster": {
        "LoadBalancingPolicy": "RoundRobin",
        "Destinations": {
          "d1": { "Address": "http://ordering-api:8080/" }
        }
      }
    }
  }
}
```

### Resilience

- YARP сам не заменяет Polly для внешних проверок/резолвера email.
- Для proxy-пути использовать:
  - passive health via YARP config,
  - active health probes,
  - отдельные HttpClient Polly policies для `IAccountEmailResolver`.

### Header трансформации

- Forward `Authorization` (по умолчанию сохраняется).
- Добавлять/пробрасывать `X-Correlation-ID`.
- Добавлять `X-User-Id` из claims (внутренний технический заголовок, опционально).

### Rate limiting

- Использовать встроенный `AddRateLimiter` как в сервисах.
- Глобально 100 req/min per IP + более строгие named policies для критичных маршрутов.

---

## 6. Simulation Layer

### Архитектура

`SimulationDecisionMiddleware`:
1. Читает глобальный флаг `Simulation:Enabled`.
2. Находит `routeId`/profile по endpoint metadata.
3. Учитывает override через заголовок `X-Simulate: true` (только для Dev/Sandbox).
4. Пишет в `HttpContext.Items`:
   - `Simulation.Enabled`
   - `Simulation.Profile`
   - `Simulation.DecisionReason`

`SimulationResponseMiddleware`:
1. Если симуляция отключена — `next()`.
2. Применяет delay (`Min/Max`, jitter).
3. С вероятностью `ErrorRate` отдает 500/503/timeout.
4. Иначе возвращает шаблонный JSON через `ISimulationResponseFactory`.
5. Записывает telemetry + event в email queue (если configured).

### SimulationOptions

```json
{
  "Simulation": {
    "Enabled": true,
    "AllowHeaderOverride": true,
    "Routes": {
      "orders-route": {
        "DelayMs": { "Min": 50, "Max": 200 },
        "ErrorRate": 0.05,
        "FailureModes": [ "500", "503", "timeout" ],
        "ResponseTemplate": "orders_list"
      }
    }
  }
}
```

### Email triggers в simulation

События для уведомлений:
- `SimulationFailureTriggered`
- `CircuitBreakerOpened`
- `RateLimitRejected`
- `CriticalRouteCompleted` (например `/payments`, `/auth/register`)

Адрес получателя:
1. Email claim из JWT (`email` / `ClaimTypes.Email`),
2. fallback: `IAccountEmailResolver` через Identity API (`/api/v1/users/{id}/contact` уже используется в Notification),
3. fallback: не отправлять письмо, только warning log.

---

## 7. Email Notification Service

### Сценарии отправки

- Опциональный audit email (feature flag).
- Симулированный downstream failure.
- Circuit breaker open event.
- Rate limit exceeded.
- Успешная критическая операция.
- Периодический digest (background service, batching из channel).

### Архитектура интерфейсов

```csharp
namespace EShop.ApiGateway.Notifications;

public interface IEmailNotificationService
{
    Task QueueAsync(EmailNotificationContext context, CancellationToken ct = default);
}

public interface IAccountEmailResolver
{
    Task<string?> ResolveByUserIdAsync(string userId, ClaimsPrincipal user, CancellationToken ct = default);
}

public interface IEmailTemplateEngine
{
    Task<(string Subject, string HtmlBody)> RenderAsync(string templateKey, object model, CancellationToken ct = default);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
```

### Реализация

- `EmailSender` на MailKit (по аналогии с `Notification.Infrastructure/Services/EmailService.cs`).
- `GatewayEmailQueue` на `Channel<EmailNotificationContext>` + hosted background dispatcher.
- Retry (экспоненциальный backoff, max 3–5), fallback в structured log при исчерпании.
- Важно: отправка **не блокирует proxy pipeline**.

### Шаблоны

- `Templates/simulation-failure.html`
- `Templates/rate-limit.html`
- `Templates/circuit-breaker-open.html`
- `Templates/critical-operation-success.html`
- `Templates/digest.html`

Хранение: файлы `Content` (CopyToOutputDirectory) — проще править без ребилда шаблонизатора.

### SMTP config

```json
{
  "Email": {
    "Host": "",
    "Port": 587,
    "UseSsl": true,
    "Username": "",
    "Password": "",
    "From": "gateway@example.com",
    "FromName": "API Gateway"
  }
}
```

Для local-dev: `appsettings.Development.Local.json` с local SMTP credentials (в стиле Notification сервиса).

---

## 8. Middleware Pipeline

Рекомендуемый порядок (в стиле существующих сервисов):

1. `UseGlobalExceptionHandler()`
2. `UseForwardedHeaders()` (если configured)
3. `UseEShopRequestLogging()`
4. `UseCors("AllowFrontend")`
5. `UseRateLimiter()`
6. `UseAuthentication()`
7. `UseAuthorization()`
8. `UseMiddleware<CorrelationIdMiddleware>()`
9. `UseMiddleware<SimulationDecisionMiddleware>()`
10. `MapWhen(simulationEnabled, simulation branch)`
11. `MapReverseProxy()`
12. Метрики и health endpoints.

Контекстные ключи в `HttpContext.Items`:
- `CorrelationId`
- `RouteId`
- `SimulationProfile`
- `SimulationResult`
- `EmailTriggerEvents` (list)

---

## 9. Аутентификация и авторизация

Фактический baseline:
- Все API валидируют JWT Bearer с `Issuer/Audience/SecretKey`.
- Identity выступает issuer (`JwtSettings:Issuer = EShop.Identity`).
- Для внутренних вызовов есть API key pattern (`InternalServiceAuth`).

Для Gateway:
- Настроить JWT как в `Catalog/Ordering/Identity Program.cs`:
  - `ValidateIssuer = true`
  - `ValidateAudience = true`
  - `ValidateLifetime = true`
  - `ValidateIssuerSigningKey = true`
  - `ClockSkew = TimeSpan.Zero`
- Политики:
  - `Admin`
  - `Authenticated`
  - `InternalService` (если нужна служебная плоскость управления симуляцией)
- Email claims extraction:
  - `ClaimTypes.Email` / `email`
  - userId: `ClaimTypes.NameIdentifier` / `sub`

---

## 10. Логирование и трейсинг

### Serilog

Выровнять с текущими API (`Program.cs` в сервисах):
- Bootstrap logger + `UseSerilog(...)`
- Enrichers: `FromLogContext`, `WithEnvironmentName`, `WithMachineName`, `WithThreadId`, `WithProperty("Application", "EShop.ApiGateway")`
- Sinks: Console + File + Seq.

### OpenTelemetry

Использовать существующий extension `AddEShopOpenTelemetry(...)` из BuildingBlocks.

Дополнительно для gateway:
- `ActivitySource("EShop.ApiGateway")`
- spans:
  - `gateway.simulation.evaluate`
  - `gateway.simulation.respond`
  - `gateway.email.enqueue`
  - `gateway.email.send`

### Метрики

- `gateway_requests_total{route,mode}`
- `gateway_request_duration_ms{route,mode}`
- `gateway_simulated_failures_total{route,code}`
- `gateway_emails_sent_total{template,status}`
- `gateway_rate_limited_total{route}`

---

## 11. Health Checks

- `/health` (общий)
- `/health/ready` (downstream + smtp + queue)
- `/health/live` (процесс жив)

Custom checks:
- `DownstreamHealthCheck` (пинг ключевых маршрутов/кластеров YARP)
- `SmtpGatewayHealthCheck` (по аналогии `Notification.Infrastructure/HealthChecks/SmtpHealthCheck.cs`)
- `EmailQueueHealthCheck` (длина/задержка очереди)

---

## 12. Docker и инфраструктура

### Dockerfile

Использовать тот же template, что у сервисов:
- `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build`
- `FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final`
- non-root `appuser`
- `curl` для HEALTHCHECK
- `ASPNETCORE_URLS=http://+:8080`

### docker-compose

Добавить `api-gateway` сервис:
- depends_on: `identity-api`, `catalog-api`, `basket-api`, `ordering-api`, `payment-api`, `notification-api`, `mailpit`.
- env:
  - `JwtSettings__*`
  - `ReverseProxy__*`
  - `Simulation__Enabled`
  - `Email__Host/Port/...`
  - `Serilog__WriteTo__...__serverUrl`
  - `OpenTelemetry__OtlpEndpoint`

---

## 13. Тестирование

### Unit Tests

- `SimulationDecisionMiddlewareTests`
  - route profile resolution
  - header override behavior
  - disabled simulation branch
- `SimulationResponseFactoryTests`
  - template rendering
  - error mode selection distribution
- `EmailNotificationServiceTests`
  - enqueue non-blocking behavior
  - retry on transient SMTP error
- `AccountEmailResolverTests`
  - resolve from claims
  - resolve via Identity API fallback

### Integration Tests

- `WebApplicationFactory<Program>` + in-memory config override routes/clusters.
- Routing tests:
  - `/api/v1/products` -> catalog cluster
  - header transforms include correlation id
- Simulation tests:
  - forced simulated 503
  - delay range respected
- Notifications tests:
  - fake SMTP (`smtp4dev`/`Mailpit` test container) captures outbound email
  - rate-limit reject triggers email event

### Текущие практики, которые нужно повторить

- NUnit + Moq stack (как в существующих `tests/Services/*`).
- WebApplicationFactory pattern (см. `IdentityApiFactory`, `BasketApiFactory`).
- Для messaging-like сценариев — MassTransit TestHarness (Notification tests).

---

## 14. Program.cs скелет

```csharp
using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Health;
using EShop.ApiGateway.Middleware;
using EShop.ApiGateway.Notifications;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.Local.json", optional: true, reloadOnChange: true);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

builder.Host.UseSerilog((context, services, cfg) => cfg
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("Application", "EShop.ApiGateway"));

// 1) Options
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection(SimulationOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));

// 2) Auth
var jwt = builder.Configuration.GetSection("JwtSettings");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SecretKey"]!)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Authenticated", p => p.RequireAuthenticatedUser());
    options.AddPolicy("Admin", p => p.RequireRole("Admin"));
});

// 3) OpenTelemetry
builder.Services.AddEShopOpenTelemetry(
    builder.Configuration,
    serviceName: "EShop.ApiGateway",
    serviceVersion: "1.0.0",
    environment: builder.Environment,
    additionalSources: "EShop.ApiGateway");

// 4) YARP
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 5) Simulation + notifications
builder.Services.AddSingleton<ISimulationProfileProvider, SimulationProfileProvider>();
builder.Services.AddSingleton<ISimulationResponseFactory, SimulationResponseFactory>();
builder.Services.AddSingleton<IEmailTemplateEngine, TemplateEngine>();
builder.Services.AddScoped<IAccountEmailResolver, IdentityAccountEmailResolver>();
builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();
builder.Services.AddSingleton<IEmailNotificationService, EmailNotificationService>();
builder.Services.AddHostedService<GatewayEmailDispatcher>();

// 6) Health
builder.Services.AddHealthChecks()
    .AddCheck<DownstreamHealthCheck>("downstream", tags: ["ready"])
    .AddCheck<SmtpGatewayHealthCheck>("smtp", tags: ["ready"]);

builder.Services.AddRateLimiter();
builder.Services.AddCors(o => o.AddPolicy("AllowFrontend", p => p.AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseGlobalExceptionHandler();
app.UseEShopRequestLogging();
app.UseCors("AllowFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SimulationDecisionMiddleware>();
app.UseMiddleware<EmailTriggerMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapReverseProxy();
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live");
app.MapMetrics("/prometheus");
app.UseEShopOpenTelemetryPrometheus();

app.Run();
public partial class Program;
```

---

## 15. Ключевые абстракции

```csharp
public interface ISimulationProfileProvider
{
    bool TryGet(string routeId, out SimulationProfile profile);
}

public interface ISimulationResponseFactory
{
    Task<IResult> CreateAsync(HttpContext context, SimulationProfile profile, CancellationToken ct);
}

public record SimulationProfile(
    string RouteId,
    int DelayMinMs,
    int DelayMaxMs,
    double ErrorRate,
    string[] FailureModes,
    string ResponseTemplate);

public record EmailNotificationContext(
    string EventType,
    string? UserId,
    string? UserEmail,
    string Route,
    int? StatusCode,
    string CorrelationId,
    DateTime OccurredAtUtc,
    Dictionary<string, string>? Metadata = null);
```

---

## 16. Этапы разработки

| Этап | Задачи | Оценка |
|---|---|---:|
| 1 | Базовый YARP proxy (routes/clusters/transforms), auth, health | 2 дн |
| 2 | Simulation middleware + profiles + response factory | 2–3 дн |
| 3 | Email module (queue, template engine, MailKit sender, resolver) | 2–3 дн |
| 4 | Интеграция trigger-логики (simulation, ratelimit, circuitbreaker) | 1–2 дн |
| 5 | Observability (OTel activities + metrics + structured logs) | 1–2 дн |
| 6 | Unit + Integration tests (включая fake SMTP) | 3 дн |
| 7 | Docker-compose интеграция + hardening + docs | 1–2 дн |

---

## 17. Риски и митигация

| Риск | Вероятность | Митигация |
|---|---|---|
| Email отправка тормозит proxy pipeline | Средняя | Channel-based async dispatch, fire-and-forget enqueue |
| SMTP недоступен | Средняя | Retry + fallback structured logging + health degraded |
| Неверная симуляция в production | Средняя | Environment guard + explicit feature flag + admin-protected toggle |
| Утечка чувствительных данных в логах | Средняя | Использовать safe logging и redaction-подход из существующих behaviors |
| Несовместимость auth claims | Низкая | Единый claims resolver (`sub`, `nameidentifier`, `email`) + integration tests |
| Неконсистентные package versions | Средняя | Придерживаться версий из `_tmp_packages_summary.json` |
| Недостаточное покрытие simulation edge-cases | Средняя | Property-based/randomized unit tests + deterministic mode в тестах |

---

## Примечания по соответствию текущему репозиторию

1. Ветка уже содержит `EShop.ApiGateway` с TODO-реализацией — этот план подразумевает **эволюцию текущего проекта**, а не параллельный gateway.
2. В проекте уже принят local-dev pattern для секретов (`appsettings.Development.Local.json`) — следует повторить для gateway SMTP/JWT/cluster URLs.
3. Тестовый стек в репо = NUnit/Moq/WebApplicationFactory; план специально не вводит альтернативные фреймворки.
