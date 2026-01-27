# 📖 Глосарій термінів

## A

### Aggregate (Агрегат)
**DDD pattern**. Група пов'язаних об'єктів, які розглядаються як єдина одиниця. Має кореневу сутність (Aggregate Root), через яку відбувається весь доступ.

**Приклад**: `Order` (aggregate root) + `OrderItems` + `Address`

### API Gateway (Шлюз API)
Єдина точка входу для всіх клієнтських запитів. Виконує routing, authentication, rate limiting.

**У проекті**: YARP (Yet Another Reverse Proxy)

### AOF (Append Only File)
Механізм persistence в Redis. Записує кожну операцію зміни даних у файл.

---

## B

### Basket (Кошик)
Сервіс для управління shopping cart. Зберігає вибрані товари користувача перед оформленням замовлення.

**Storage**: Redis (in-memory + persistence)

### Bounded Context (Обмежений контекст)
**DDD pattern**. Чітко визначена межа, в якій діє певна domain model.

**У проекті**: 
- Identity Context (users, roles)
- Catalog Context (products, categories)
- Ordering Context (orders, payments)

### BuildingBlocks
Спільні бібліотеки (shared libraries), які використовуються всіма мікросервісами.

**Приклад**: `EShop.Shared.Contracts`, `EShop.Shared.EventBus`

---

## C

### Circuit Breaker (Вимикач)
**Resilience pattern**. Запобігає cascade failures. Якщо сервіс падає, Circuit Breaker "відкривається" і не пропускає запити певний час.

**Бібліотека**: Polly

### CQRS (Command Query Responsibility Segregation)
Розділення читання (queries) та запису (commands) на різні моделі.

**У проекті**: MediatR для реалізації

### Correlation ID
Унікальний ідентифікатор запиту, який передається через всі сервіси для tracing.

**Header**: `X-Correlation-ID`

---

## D

### DDD (Domain-Driven Design)
Підхід до проектування, який фокусується на бізнес-логіці (domain) та її modeling.

**Основні концепції**: Entities, Value Objects, Aggregates, Domain Events

### Dead Letter Queue (DLQ)
Черга для повідомлень, які не вдалося обробити після N спроб.

**У проекті**: RabbitMQ DLQ для failed events

### DTO (Data Transfer Object)
Об'єкт для передачі даних між layers або сервісами. Не містить бізнес-логіки.

**Приклад**: `ProductDto`, `OrderDto`

---

## E

### Entity
Об'єкт з унікальним ідентифікатором (ID). Два entity з різними ID - різні об'єкти.

**Приклад**: `Product`, `Order`, `User`

### Event Bus
Механізм для асинхронної комунікації між сервісами через events.

**У проекті**: MassTransit + RabbitMQ

### Event-Driven Architecture
Архітектура, де сервіси комунікують через events (подій), а не direct calls.

**Приклади events**: `BasketCheckoutEvent`, `OrderCreatedEvent`

---

## F

### Feature Flag
Механізм для ввімкнення/вимкнення функціональності без redeploy.

**Приклад**: `NewCheckoutFlow`, `PaymentProviderStripe`

---

## G

### Grafana
Platform для візуалізації metrics. Створює dashboards на основі даних з Prometheus.

**URL**: http://localhost:3000

---

## H

### Health Check
Endpoint для перевірки здоров'я сервісу.

**Endpoints**:
- `/health/live` - чи працює application
- `/health/ready` - чи готовий приймати requests

### Hangfire
Бібліотека для background job processing в .NET.

**Use cases**: Scheduled tasks, retry logic для failed jobs

---

## I

### Idempotency (Ідемпотентність)
Властивість операції: виконання N разів дає той самий результат, що й 1 раз.

**Важливо для**: Event handlers (щоб дублікати events не створювали дублікати замовлень)

### Integration Event
Event, який публікується одним мікросервісом і споживається іншими.

**Приклад**: `OrderCreatedEvent` (Ordering → Payment, Notification)

---

## J

### Jaeger
Platform для distributed tracing. Показує шлях запиту через всі мікросервіси.

**URL**: http://localhost:16686

### JWT (JSON Web Token)
Токен для автентифікації. Містить claims (user ID, roles, etc.).

**Структура**: Header.Payload.Signature

---

## K

### k6
Load testing tool. Написаний на Go, скрипти на JavaScript.

**Приклад**: `k6 run load-test.js --vus 100`

---

## M

### MassTransit
Distributed application framework для .NET. Wrapper над message brokers (RabbitMQ, Azure Service Bus).

**Features**: Retry policies, Saga, message routing

### MediatR
Бібліотека для реалізації mediator pattern. Використовується для CQRS.

**Приклад**: `await _mediator.Send(new CreateProductCommand())`

### Message Broker
Проміжний сервер для асинхронного обміну повідомленнями.

**У проекті**: RabbitMQ

### Microservice (Мікросервіс)
Невеликий, автономний сервіс, який виконує одну business capability.

**У проекті**: 7 мікросервісів (Identity, Catalog, Basket, Ordering, Payment, Notification, Gateway)

---

## O

### OpenTelemetry
Vendor-neutral standard для observability (logs, metrics, traces).

**Backend**: Jaeger (traces), Prometheus (metrics)

### Outbox Pattern
Pattern для гарантії доставки events. Event спочатку зберігається в БД, потім публікується.

---

## P

### Polly
Resilience library для .NET.

**Patterns**: Retry, Circuit Breaker, Timeout, Bulkhead, Fallback

### Prometheus
Time-series database для metrics.

**URL**: http://localhost:9090

---

## R

### Rate Limiting
Обмеження кількості requests від одного користувача за певний час.

**Приклад**: 100 requests/minute per user

### Refresh Token
Довгоживучий токен для отримання нового access token без повторного login.

**Lifetime**: 7 днів (access token - 15 хвилин)

### Resilience
Здатність системи відновлюватися після failures.

**Patterns**: Circuit Breaker, Retry, Timeout

### Repository Pattern
Абстракція для data access. Приховує деталі роботи з БД.

**Приклад**: `IProductRepository`, `IOrderRepository`

### RTO (Recovery Time Objective)
Максимально допустимий час downtime.

**У проекті**: 15 хвилин для critical services

### RPO (Recovery Point Objective)
Максимально допустима втрата даних (у часі).

**У проекті**: 0 для transactional data (Orders)

---

## S

### Saga
Distributed transaction pattern. Серія локальних транзакцій з компенсаціями при failure.

**Приклад**: Order → Payment → Inventory (якщо Payment fails → compensate Order)

### Seq
Structured logging server. Централізоване зберігання та пошук логів.

**URL**: http://localhost:5341

### Serilog
Structured logging library для .NET.

**Sinks**: Console, Seq, File, Elasticsearch

### SLA (Service Level Agreement)
Договір про гарантований uptime сервісу.

**Приклад**: 99.9% uptime (8.76 годин downtime/рік)

### SLO (Service Level Objective)
Внутрішня ціль по uptime/performance (strict than SLA).

**Приклад**: SLO 99.95%, SLA 99.9%

---

## T

### TestContainers
Бібліотека для запуску Docker контейнерів у integration tests.

**Приклад**: PostgreSQL, Redis контейнери для тестів

### Tracing (Distributed Tracing)
Відстеження шляху запиту через всі мікросервіси.

**Tool**: Jaeger + OpenTelemetry

---

## V

### Value Object
Об'єкт без identity. Два Value Object з однаковими values - еквівалентні.

**Приклад**: `Money`, `Address`, `Email`

---

## W

### WAL (Write-Ahead Logging)
Механізм у PostgreSQL. Всі зміни спочатку пишуться у log, потім у БД.

**Use case**: Point-in-time recovery

---

## Y

### YARP (Yet Another Reverse Proxy)
Reverse proxy від Microsoft для .NET.

**Use case**: API Gateway

---

## Акроніми та абревіатури

| Акронім | Розшифровка | Пояснення |
|---------|-------------|-----------|
| **API** | Application Programming Interface | Інтерфейс для взаємодії між програмами |
| **CQRS** | Command Query Responsibility Segregation | Розділення reads/writes |
| **DDD** | Domain-Driven Design | Підхід до проектування з фокусом на domain |
| **DLQ** | Dead Letter Queue | Черга для failed messages |
| **DTO** | Data Transfer Object | Об'єкт для передачі даних |
| **EF** | Entity Framework | ORM для .NET |
| **JWT** | JSON Web Token | Токен для автентифікації |
| **ORM** | Object-Relational Mapping | Маппінг між objects та DB tables |
| **REST** | Representational State Transfer | Архітектурний стиль для API |
| **SPA** | Single Page Application | Frontend application (React) |
| **TTL** | Time To Live | Час життя (кеш, токен, etc.) |
| **WAL** | Write-Ahead Logging | Log-based persistence |

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
