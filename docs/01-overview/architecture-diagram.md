# 📊 Архітектурна діаграма

## Повна архітектура системи

```
                                    ┌─────────────────┐
                                    │   Клієнти       │
                                    │  (React SPA)    │
                                    └────────┬────────┘
                                             │
                                    ┌────────▼────────┐
                                    │  API Gateway    │
                                    │    (YARP)       │
                                    └────────┬────────┘
                                             │
        ┌────────────────────────────────────┼────────────────────────────────────┐
        │                                    │                                    │
┌───────▼───────┐  ┌─────────▼─────────┐  ┌──▼──────────┐  ┌──────────▼─────────┐
│ Identity      │  │ Catalog Service   │  │ Basket      │  │ Order Service      │
│ Service       │  │                   │  │ Service     │  │                    │
│ (Auth/Users)  │  │ (Products/        │  │ (Cart/      │  │ (Orders/           │
│               │  │  Categories)      │  │  Redis)     │  │  Processing)       │
└───────┬───────┘  └─────────┬─────────┘  └──┬──────────┘  └──────────┬─────────┘
        │                    │               │                        │
        │          ┌─────────▼─────────┐     │             ┌──────────▼─────────┐
        │          │ PostgreSQL        │     │             │ PostgreSQL         │
        │          │ (Catalog DB)      │     │             │ (Orders DB)        │
        │          └───────────────────┘     │             └────────────────────┘
        │                                    │
┌───────▼───────┐                   ┌────────▼────────┐
│ PostgreSQL    │                   │     Redis       │
│ (Identity DB) │                   │   (Basket +     │
└───────────────┘                   │    Caching)     │
                                    └─────────────────┘
        
                    ┌─────────────────────────────────┐
                    │        Message Bus              │
                    │       (RabbitMQ)                │
                    └──────────────┬──────────────────┘
                                   │
        ┌──────────────────────────┼──────────────────────────┐
        │                          │                          │
┌───────▼───────┐         ┌────────▼────────┐        ┌────────▼────────┐
│ Notification  │         │ Payment         │        │ Background      │
│ Service       │         │ Service         │        │ Jobs Service    │
│ (Email/SMS)   │         │ (Mock/Stub)     │        │ (Hangfire)      │
└───────────────┘         └─────────────────┘        └─────────────────┘

                    ┌─────────────────────────────────┐
                    │     Observability Stack         │
                    │  Prometheus + Grafana + Seq     │
                    └─────────────────────────────────┘
```

---

## Пояснення компонентів

### Frontend Layer

#### React SPA
- **Призначення**: Single Page Application для користувачів
- **Технологія**: React 18 + TypeScript
- **Взаємодія**: HTTP/HTTPS через API Gateway
- **Features**:
  - Responsive UI (desktop + mobile)
  - State management (Zustand)
  - Real-time updates (опційно SignalR)

---

### API Gateway Layer

#### YARP Reverse Proxy
- **Призначення**: Єдина точка входу для всіх API запитів
- **Responsibilities**:
  - ✅ Request routing до відповідних сервісів
  - ✅ JWT валідація (перевірка токена)
  - ✅ Rate limiting (захист від abuse)
  - ✅ CORS handling
  - ✅ Request/Response logging
  - ✅ SSL termination
- **Ports**: 
  - `5000` (HTTP)
  - `5001` (HTTPS)

---

### Business Services Layer

#### 1. Identity Service
**Domain**: User management та authentication

**Responsibilities**:
- ✅ Реєстрація користувачів
- ✅ Автентифікація (login/logout)
- ✅ JWT токени (access + refresh)
- ✅ 2FA (TOTP)
- ✅ OAuth2 (Google, GitHub)
- ✅ Password reset
- ✅ Управління ролями

**Database**: PostgreSQL (`identity` DB)

**Ports**: `5001` (internal)

**API Endpoints**:
```
POST /api/v1/auth/register
POST /api/v1/auth/login
POST /api/v1/auth/refresh
GET  /api/v1/auth/me
```

---

#### 2. Catalog Service
**Domain**: Product та category management

**Responsibilities**:
- ✅ CRUD операції для продуктів
- ✅ Категорії з ієрархією
- ✅ Пошук та фільтрація
- ✅ Пагінація (cursor-based)
- ✅ Завантаження зображень
- ✅ Кешування в Redis
- ✅ Publishing events при змінах

**Database**: PostgreSQL (`catalog` DB)

**Cache**: Redis (product lists, single products)

**Ports**: `5002` (internal)

**API Endpoints**:
```
GET    /api/v1/products
GET    /api/v1/products/{id}
POST   /api/v1/products         (Admin only)
PUT    /api/v1/products/{id}    (Admin only)
DELETE /api/v1/products/{id}    (Admin only)
GET    /api/v1/categories
```

**Published Events**:
- `ProductCreatedEvent`
- `ProductUpdatedEvent`
- `ProductPriceChangedEvent`
- `ProductStockChangedEvent`

---

#### 3. Basket Service
**Domain**: Shopping cart management

**Responsibilities**:
- ✅ Додавання/видалення товарів
- ✅ Оновлення кількості
- ✅ Зберігання в Redis (швидко + persistence)
- ✅ Checkout → publishing `BasketCheckoutEvent`
- ✅ Merge anonymous basket при логіні

**Storage**: Redis (key-value, TTL 30 днів)

**Ports**: `5003` (internal)

**API Endpoints**:
```
GET    /api/v1/basket/{customerId}
POST   /api/v1/basket
DELETE /api/v1/basket/{customerId}
POST   /api/v1/basket/checkout
```

**Published Events**:
- `BasketCheckoutEvent` → Ordering Service

---

#### 4. Ordering Service
**Domain**: Order processing та lifecycle management

**Responsibilities**:
- ✅ Створення замовлень з basket checkout
- ✅ Order state management (Pending → Paid → Shipped → Completed)
- ✅ Order history для користувачів
- ✅ Admin перегляд всіх замовлень
- ✅ CQRS pattern (MediatR)
- ✅ Domain events

**Database**: PostgreSQL (`ordering` DB)

**Ports**: `5004` (internal)

**API Endpoints**:
```
GET  /api/v1/orders              (my orders)
GET  /api/v1/orders/{id}
POST /api/v1/orders/{id}/cancel
GET  /api/v1/orders/admin        (all orders, Admin)
```

**Consumed Events**:
- `BasketCheckoutEvent` → Create order
- `PaymentCompletedEvent` → Update order status to Paid

**Published Events**:
- `OrderCreatedEvent` → Payment Service, Notification Service
- `OrderShippedEvent` → Notification Service
- `OrderCancelledEvent` → Notification Service

---

#### 5. Payment Service
**Domain**: Payment processing (mock)

**Responsibilities**:
- ✅ Mock payment обробка
- ✅ Симуляція success/failure
- ✅ Publishing payment results

**Logic**:
```csharp
// Карти ending in 0000 = declined
// Все інше = success
await Task.Delay(2000); // Simulate processing
```

**Ports**: `5005` (internal)

**Consumed Events**:
- `OrderCreatedEvent` → Process payment

**Published Events**:
- `PaymentCompletedEvent` → Ordering Service
- `PaymentFailedEvent` → Ordering Service

---

#### 6. Notification Service
**Domain**: Email/SMS notifications

**Responsibilities**:
- ✅ Email відправка (MailKit/SendGrid)
- ✅ Template-based emails
- ✅ Background processing (worker service)

**Ports**: `5006` (internal, worker)

**Consumed Events**:
- `OrderCreatedEvent` → "Ваше замовлення прийнято"
- `PaymentCompletedEvent` → "Оплата успішна"
- `OrderShippedEvent` → "Замовлення відправлено"
- `OrderCancelledEvent` → "Замовлення скасовано"

---

### Data Layer

#### PostgreSQL Databases
**Version**: 16 (Alpine)

**Databases**:
- `identity` - Users, roles, claims
- `catalog` - Products, categories, images
- `ordering` - Orders, order items, addresses

**Connection Pooling**: 
- Min: 5 connections
- Max: 100 connections per service

**Backup Strategy**:
- Daily automated backups
- WAL archiving для point-in-time recovery
- 30 днів retention

---

#### Redis Cache
**Version**: 7 (Alpine)

**Use Cases**:
1. **Basket Storage** (primary)
   - Key: `basket:{customerId}`
   - TTL: 30 днів
   - Persistence: AOF (Append-Only File)

2. **Cache для Catalog** (secondary)
   - Product lists: 5 хв TTL
   - Single product: 10 хв TTL
   - Categories: 30 хв TTL

3. **Distributed Cache** (sessions, temp data)
   - Refresh tokens: 7 днів TTL
   - Session data: 20 хв sliding expiration

**Configuration**:
- Max memory: 256MB
- Eviction policy: `allkeys-lru`
- Persistence: `appendonly yes`

---

### Messaging Layer

#### RabbitMQ Message Broker
**Version**: 3 (Management Alpine)

**Exchanges**:
```
eshop.events (topic exchange)
├── basket.checkout        → order-service queue
├── order.created          → payment-service, notification-service queues
├── payment.completed      → order-service queue
└── order.shipped          → notification-service queue
```

**Queues**:
- `order-service` - orders processing
- `payment-service` - payment processing
- `notification-service` - email sending

**Features**:
- ✅ Message persistence (durable queues)
- ✅ Dead Letter Queue (DLQ) для failed messages
- ✅ Retry with exponential backoff
- ✅ Circuit breaker per consumer
- ✅ Idempotency через event deduplication

**Management UI**: `http://localhost:15672` (guest/guest)

---

### Observability Stack

#### Serilog + Seq
**Structured Logging**

**Seq Server**: 
- URL: `http://localhost:5341`
- Centralized log aggregation
- Full-text search
- Filtering по properties

**Log Levels**:
- `Debug`: Development only
- `Information`: Normal operations
- `Warning`: Degraded state
- `Error`: Handled exceptions
- `Fatal`: Unhandled crashes

**Enrichers**:
- Machine name
- Environment (Development/Production)
- Correlation ID (X-Correlation-ID header)
- User ID (from JWT claims)

---

#### Prometheus + Grafana
**Metrics та Dashboards**

**Prometheus**:
- URL: `http://localhost:9090`
- Scraping interval: 15 seconds
- Retention: 15 days

**Metrics Collected**:
- HTTP request duration (histogram)
- HTTP requests total (counter)
- Database query duration
- Cache hit/miss rates
- Queue depth (RabbitMQ)
- Memory usage, CPU usage

**Grafana**:
- URL: `http://localhost:3000`
- Pre-configured dashboards:
  - ASP.NET Core metrics
  - PostgreSQL metrics
  - Redis metrics
  - RabbitMQ metrics

---

#### OpenTelemetry + Jaeger
**Distributed Tracing**

**Jaeger UI**: 
- URL: `http://localhost:16686`
- Trace end-to-end requests across services

**Trace Example**:
```
API Gateway (100ms)
├── Identity Service (20ms) - JWT validation
└── Catalog Service (80ms)
    ├── Cache lookup (5ms) - Miss
    ├── Database query (50ms)
    └── Cache set (5ms)
```

**Propagation**:
- W3C Trace Context standard
- Baggage для user context

---

## Request Flow Examples

### Example 1: User Registration
```
1. Frontend → API Gateway → Identity Service
2. Identity Service → PostgreSQL (create user)
3. Identity Service → Email Service (send confirmation)
4. Return JWT tokens to Frontend
```

### Example 2: Product Search
```
1. Frontend → API Gateway → Catalog Service
2. Catalog Service → Redis (check cache)
3. [Cache Miss] → PostgreSQL (query products)
4. Catalog Service → Redis (cache results, 5 min TTL)
5. Return products to Frontend
```

### Example 3: Checkout Flow (End-to-End)
```
1. Frontend → API Gateway → Basket Service
2. Basket Service → Redis (get basket items)
3. Basket Service → RabbitMQ (publish BasketCheckoutEvent)
4. [Async] Ordering Service consumes BasketCheckoutEvent
5. Ordering Service → PostgreSQL (create order)
6. Ordering Service → RabbitMQ (publish OrderCreatedEvent)
7. [Async] Payment Service consumes OrderCreatedEvent
8. Payment Service → Mock processing (2 sec delay)
9. Payment Service → RabbitMQ (publish PaymentCompletedEvent)
10. [Async] Ordering Service consumes PaymentCompletedEvent
11. Ordering Service → PostgreSQL (update order status = Paid)
12. [Async] Notification Service sends email
13. Frontend polls Order API → sees order status "Paid"
```

---

## Deployment Diagram

### Development (Docker Compose)
```
docker-compose up
├── postgres (port 5432)
├── redis (port 6379)
├── rabbitmq (ports 5672, 15672)
├── seq (port 5341)
├── identity-api (internal)
├── catalog-api (internal)
├── basket-api (internal)
├── ordering-api (internal)
├── payment-api (internal)
├── notification-worker (internal)
├── api-gateway (ports 5000, 5001)
└── eshop-spa (port 3000)
```

### Production (Kubernetes) - Optional
```
Namespace: eshop-production
├── Deployments (3 replicas each):
│   ├── identity-deployment
│   ├── catalog-deployment
│   ├── basket-deployment
│   └── ordering-deployment
├── Services (ClusterIP):
│   ├── identity-svc
│   ├── catalog-svc
│   └── ...
├── StatefulSets:
│   ├── postgres-statefulset
│   ├── redis-statefulset
│   └── rabbitmq-statefulset
├── Ingress:
│   └── api-gateway (LoadBalancer)
└── ConfigMaps & Secrets
```

---

## Security Diagram

```
Internet → [Cloudflare/WAF] → [Load Balancer] → API Gateway
                                                      │
                                    ┌─────────────────┼─────────────────┐
                                    │                 │                 │
                            JWT Validation    Rate Limiting      CORS Check
                                    │                 │                 │
                                    └─────────────────▼─────────────────┘
                                              Internal Network
                                         (services communicate)
```

**Security Layers**:
1. **Edge**: HTTPS enforced, rate limiting
2. **Gateway**: JWT validation, CORS
3. **Service**: Authorization checks (roles/claims)
4. **Data**: Encrypted at rest (PostgreSQL), Redis auth

---

## Посилання на детальні діаграми

- [Технологічний стек](tech-stack.md)
- [Communication Patterns](../03-architecture/communication-patterns.md)
- [Event Flow Diagrams](../05-infrastructure/message-broker.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
