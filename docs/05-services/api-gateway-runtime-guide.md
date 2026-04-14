# YARP API Gateway — детальное руководство по настройке и использованию

> Актуально для текущей реализации в `src/ApiGateways/EShop.ApiGateway` (проект на `.NET 10`).

---

## 1. Что это за сервис

`EShop.ApiGateway` — это входная точка для клиентских запросов в микросервисную систему.

Он выполняет сразу несколько ролей:

- reverse proxy (YARP) к downstream-сервисам;
- simulation layer (имитация ответов/ошибок по маршрутам);
- JWT-аутентификация и авторизация;
- rate limiting;
- email-trigger события (через асинхронную очередь);
- health/readiness/liveness;
- observability (Serilog + OpenTelemetry + Prometheus).

Ключевые файлы:

- `Program.cs` — композиция всего пайплайна;
- `appsettings*.json` — маршруты, кластеры, simulation, email, health thresholds;
- `Middleware/*` — middleware логика (correlation, simulation, email triggers, exceptions);
- `Simulation/*` — профили и генерация simulation-ответов;
- `Notifications/*` — очередь, шаблоны, отправка email;
- `Health/*` — health checks;
- `Telemetry/*` — кастомные метрики и activity source.

---

## 2. Как gateway запускается и что регистрирует

В `Program.cs` регистрируются:

1. **Конфигурация options**
   - `GatewayOptions`
   - `SimulationOptions`
   - `EmailOptions`
   - `EmailQueueHealthOptions`
   - `RateLimitingOptions`
   - `IdentityServiceOptions`

2. **Логирование**
   - bootstrap Serilog
   - enrichers (`Environment`, `MachineName`, `ThreadId`, `Application`)

3. **Security**
   - JWT Bearer (валидируются issuer/audience/lifetime/signing key)
   - Authorization policies (`Authenticated`, `Admin`)

4. **Rate limiting**
   - global fixed window limiter
   - отдельный limiter для simulation

5. **YARP**
   - `AddReverseProxy().LoadFromConfig("ReverseProxy")`

6. **Simulation сервисы**
   - `ISimulationProfileProvider`
   - `ISimulationResponseFactory`

7. **Email сервисы**
   - `GatewayEmailQueue`
   - `IEmailNotificationService`
   - `IEmailTemplateEngine`
   - `IEmailSender`
   - `IAccountEmailResolver` (через Identity API)
   - `GatewayEmailDispatcher` (BackgroundService)

8. **Health checks**
   - downstream config-check
   - SMTP check
   - email queue check
   - liveness check

9. **OpenAPI + OTel + metrics**

---

## 3. Порядок middleware pipeline

Текущий порядок (критично для поведения):

1. `UseGlobalExceptionHandler()`
2. `UseForwardedHeaders()` (если заданы known proxies)
3. `UseEShopRequestLogging()`
4. `CorrelationIdMiddleware`
5. `EmailTriggerMiddleware` *(после next анализирует итоговый response и решает, нужно ли ставить email в очередь)*
6. `UseCors("AllowFrontend")`
7. `UseRateLimiter()`
8. `UseHttpsRedirection()` (если задан https port)
9. `UseAuthentication()`
10. `UseAuthorization()`
11. `SimulationDecisionMiddleware`
12. `SimulationResponseMiddleware`
13. `MapReverseProxy()`
14. endpoints: `/prometheus`, `/health*`, `/`

Почему важно: `EmailTriggerMiddleware` стоит рано, чтобы видеть и simulation, и proxy, и rate-limit исходы.

---

## 4. Маршрутизация YARP (ReverseProxy)

Конфигурация находится в `appsettings.json -> ReverseProxy`.

### Routes

Сейчас настроены route-пути (pattern-based):

- `/api/v1/auth/{**catch-all}` -> `identity-cluster`
- `/api/v1/products/{**catch-all}` -> `catalog-cluster`
- `/api/v1/basket/{**catch-all}` -> `basket-cluster`
- `/api/v1/orders/{**catch-all}` -> `ordering-cluster`
- `/api/v1/payments/{**catch-all}` -> `payment-cluster`

### Clusters

Для каждого cluster задан destination (`http://<service>:8080/`), load balancing = `RoundRobin`.

---

## 5. Simulation layer: как работает

### 5.1 Решение о simulation

`SimulationDecisionMiddleware`:

- читает глобальный флаг `Simulation:Enabled`;
- ищет профиль по `PathPrefix`;
- учитывает заголовок `X-Simulate` (если разрешен `AllowHeaderOverride`);
- записывает в `HttpContext.Items`:
  - `Simulation.Enabled`
  - `Simulation.Profile`
  - `Simulation.DecisionReason`

### 5.2 Генерация simulation-ответа

`SimulationResponseMiddleware`:

- если simulation выключен — пропускает запрос в proxy;
- если включен — формирует ответ через `SimulationResponseFactory`.

`SimulationResponseFactory` поддерживает:

- задержку (`DelayMs.Min/Max`);
- вероятностные ошибки (`ErrorRate`);
- deterministic режим через `ForcedFailureMode` (`500`, `503`, `timeout`);
- шаблонные payloads (`ResponseTemplate`, например `orders_list`).

### 5.3 Настройка simulation в конфиге

Секция `Simulation:Routes` поддерживает:

- `PathPrefix`
- `Enabled`
- `DelayMs.Min/Max`
- `ErrorRate`
- `FailureModes[]`
- `ForcedFailureMode`
- `ResponseTemplate`

---

## 6. Email notifications: как и когда отправляются

### 6.1 Где принимается решение

`EmailTriggerMiddleware` анализирует итог response и формирует `EmailNotificationContext`.

Триггеры:

- `SimulationFailureTriggered` (simulation + 5xx)
- `SimulationResponse` (simulation + audit включен)
- `RateLimitExceeded` (429)
- `DownstreamFailure` (proxy + 5xx)
- `CriticalOperationCompleted` (2xx + путь в `CriticalSuccessPathPrefixes`)

### 6.2 Очередь и отправка

- `EmailNotificationService` кладет события в `GatewayEmailQueue`.
- `GatewayEmailDispatcher` в фоне читает очередь и отправляет письма.
- Если email не в claims/context — резолвит через Identity API (`IAccountEmailResolver`).
- Отправка через `MailKitEmailSender`.
- Есть retry (`MaxSendAttempts=3`) с backoff.

### 6.3 Шаблоны писем

Файлы шаблонов:

- `Templates/simulation-response.html`
- `Templates/simulation-failure.html`
- `Templates/rate-limit.html`
- `Templates/downstream-failure.html`
- `Templates/critical-success.html`

`EmailTemplateEngine`:

- выбирает template по `EventType`;
- делает token replacement (`{{Route}}`, `{{StatusCode}}`, ...);
- fallback на встроенный HTML, если файл не найден.

---

## 7. Security

### JWT

Требует корректных значений:

- `JwtSettings:SecretKey` (>= 32 chars)
- `JwtSettings:Issuer`
- `JwtSettings:Audience`

### Internal identity resolver

Для вызова Identity API по user contact:

- `IdentityService:BaseUrl`
- `IdentityService:ApiKey`
- `IdentityService:ApiKeyHeaderName`

### Local secrets

Для локальной разработки используется `appsettings.Development.Local.json` (исключен из Git).

---

## 8. Rate limiting

Настройки в `RateLimiting`:

- `GlobalPermitLimit`
- `GlobalWindowSeconds`
- `SimulationPermitLimit`
- `SimulationWindowSeconds`

При 429 генерируется trigger `RateLimitExceeded` (если включена политика в `Gateway`).

---

## 9. Observability

### Логи

Serilog sinks:
- Console
- File (`logs/gateway-*.log`)
- Seq

### Метрики

Prometheus endpoint: `/prometheus`

Кастомные метрики gateway:
- `gateway_requests_total`
- `gateway_request_duration_ms`
- `gateway_simulated_failures_total`
- `gateway_email_queued_total`
- `gateway_email_sent_total`
- `gateway_rate_limited_total`

### Tracing

- OpenTelemetry через общие extensions
- custom activity source: `EShop.ApiGateway`
- spans для simulation/email dispatch

---

## 10. Health endpoints

- `/health` — aggregate health
- `/health/ready` — readiness checks (`ready` tag)
- `/health/live` — liveness checks (`live` tag)

Текущие checks:
- `DownstreamHealthCheck`
- `SmtpGatewayHealthCheck` *(при неуказанном SMTP host -> Degraded)*
- `EmailQueueHealthCheck` *(healthy/degraded/unhealthy по backlog/drop thresholds)*
- `GatewayLivenessHealthCheck`

---

## 11. Docker и окружение

### Dockerfile

`src/ApiGateways/EShop.ApiGateway/Dockerfile`:
- multi-stage
- non-root user
- readiness healthcheck

### Compose

В `docker-compose.yml` добавлен `api-gateway`:
- порт: `${GATEWAY_API_PORT:-7000}:8080`
- depends_on downstream + mailpit
- env mappings для JWT/CORS/SMTP/Identity/OTel/Serilog
- volume: `gateway-logs`

---

## 12. Как взаимодействовать с gateway (практика)

### 12.1 Проверка доступности

```bash
curl http://localhost:7000/
curl http://localhost:7000/health/live
curl http://localhost:7000/health/ready
```

### 12.2 Simulation запрос

```bash
curl -H "X-Simulate: true" http://localhost:7000/api/v1/orders
```

### 12.3 Проксирование без simulation

```bash
curl -H "X-Simulate: false" http://localhost:7000/api/v1/orders
```

### 12.4 Просмотр метрик

```bash
curl http://localhost:7000/prometheus
```

### 12.5 Проверка email контура локально

- SMTP/инбокс в dev: Mailpit (`http://localhost:8025`)
- провоцируйте trigger (например `X-Simulate: true` + failure mode), затем проверяйте письмо в Mailpit UI.

---

## 13. Нагрузочные и проверочные инструменты

### k6

- smoke: `tests/Load/ApiGateway/k6-smoke.js`
- soak: `tests/Load/ApiGateway/k6-soak.js`

### Проверка целевого окружения

PowerShell script:

```powershell
pwsh ./scripts/gateway-verify-target.ps1 -Profile sandbox
```

Скрипт проверяет:
- compose profile up
- `/health/ready`
- `/health/live`
- `/`
- Mailpit
- simulation endpoint

---

## 14. CI/CD quality gates для gateway

Workflow:
- `.github/workflows/gateway-quality-gates.yml`

Что делает:
- guard проверки env template;
- restore/build;
- gateway unit + integration tests;
- docker build gateway image;
- `docker compose config` validation.

---

## 15. Быстрый чек-лист “готово к использованию”

1. Заполнены обязательные env/secret values (`JWT`, `INTERNAL_SERVICE_API_KEY`, SMTP при необходимости).
2. `docker compose --profile sandbox up -d` поднимается без ошибок.
3. `health/live` и `health/ready` возвращают 200.
4. Simulation и proxy сценарии отвечают ожидаемо.
5. Письма попадают в Mailpit (локально) или SMTP провайдер (целевое окружение).
6. Видны метрики `/prometheus` и логи в Seq.

---

Если нужно, могу дополнительно сделать вторую версию этого документа в формате "операторской инструкции" (runbook) с типовыми инцидентами и пошаговым troubleshooting.
