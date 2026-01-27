# 🌊 Data Flow & Communication Patterns

Документація про те, як дані рухаються через систему та як сервіси комунікують між собою.

---

## Communication Types

### 1. Synchronous Communication (HTTP/REST)

**When to use**:
- Client needs immediate response
- Simple request/response pattern
- External API calls (Stripe, SendGrid)

**Examples**:
- Frontend → API Gateway → Service
- Basket Service → Catalog Service (validate product exists)
- Admin → Catalog Service (create product)

**Pros**:
- Simple to implement
- Immediate feedback
- Easy to debug

**Cons**:
- Tight coupling
- Cascading failures
- Availability requirements (both services must be up)

---

### 2. Asynchronous Communication (RabbitMQ)

**When to use**:
- Fire-and-forget operations
- Multiple consumers for same event
- Decoupled services
- Background processing

**Examples**:
- Order created → Send email (Notification Service)
- Payment succeeded → Update order status (Ordering Service)
- Product updated → Invalidate cache

**Pros**:
- Loose coupling
- Resilience (consumers can be down temporarily)
- Scalability (multiple consumers)

**Cons**:
- Eventual consistency
- Harder to debug
- Message ordering challenges

---

## Request Flow Examples

### Example 1: User Login (Synchronous)

```
┌─────────┐     ┌─────────┐     ┌──────────┐     ┌──────────┐
│ Browser │────►│ Web App │────►│ Gateway  │────►│ Identity │
│         │     │ (React) │     │  (YARP)  │     │ Service  │
└─────────┘     └─────────┘     └──────────┘     └────┬─────┘
                                                       │
     ▲                                                 │
     │                                                 ▼
     │                                          ┌────────────┐
     │                                          │ PostgreSQL │
     │                                          │  (users)   │
     │                                          └────────────┘
     │                                                 │
     │◄────────────────────────────────────────────────┘
     │
     │ Response: { accessToken, refreshToken, user }
```

**Step-by-step**:

1. **User enters credentials** in React form
2. **POST /api/v1/auth/login** → API Gateway
3. **Gateway routes** to Identity Service
4. **Identity Service**:
   - Validates credentials against PostgreSQL
   - Generates JWT access token (15 min)
   - Generates refresh token (7 days), stores in Redis
5. **Response** returned to client
6. **Client stores** access token in memory, refresh token in httpOnly cookie

**Duration**: ~200ms (synchronous, user waits)

---

### Example 2: Create Order (Async + Sync)

```
┌─────────┐     ┌─────────┐     ┌──────────┐     ┌─────────┐
│ Browser │────►│ Web App │────►│ Gateway  │────►│ Basket  │
│         │     │ (React) │     │  (YARP)  │     │ Service │
└─────────┘     └─────────┘     └──────────┘     └────┬────┘
                                                       │
     ▲                                                 │
     │                                                 ▼
     │                                          ┌────────────┐
     │                                          │   Redis    │
     │                                          │  (basket)  │
     │                                          └────────────┘
     │                                                 │
     │                                                 │ Publish event
     │                                                 ▼
     │                                          ┌─────────────┐
     │                                          │  RabbitMQ   │
     │                                          │ (EventBus)  │
     │                                          └──────┬──────┘
     │                                                 │
     │                                        ┌────────┴────────┐
     │                                        │                 │
     │                                        ▼                 ▼
     │                                  ┌──────────┐     ┌────────────┐
     │                                  │ Ordering │     │Notification│
     │                                  │ Service  │     │  Service   │
     │                                  └────┬─────┘     └────────────┘
     │                                       │                 │
     │                                       │                 ▼
     │                                       ▼           ┌──────────┐
     │                                ┌────────────┐    │ SendGrid │
     │                                │ PostgreSQL │    │  (Email) │
     │                                │  (orders)  │    └──────────┘
     │                                └────────────┘
     │
     │◄─────────────────── Response: { orderId }
```

**Step-by-step**:

1. **POST /api/v1/basket/checkout**
2. **Basket Service**:
   - Retrieves basket from Redis
   - Validates items (check with Catalog Service via HTTP)
   - **Publishes** `BasketCheckedOutEvent` to RabbitMQ
   - Returns `orderId` immediately (synchronous response)
3. **Ordering Service** (consumes event asynchronously):
   - Receives `BasketCheckedOutEvent`
   - Creates Order entity
   - Saves to PostgreSQL
   - Publishes `OrderCreatedEvent`
4. **Notification Service** (consumes event):
   - Receives `OrderCreatedEvent`
   - Sends confirmation email via SendGrid
5. **Payment Service** (not shown):
   - Receives `OrderCreatedEvent`
   - Initiates payment

**Duration**:
- **Synchronous part** (checkout request): ~300ms
- **Asynchronous part** (email sent): ~2-5 seconds later

**Benefits**:
- User gets immediate response (doesn't wait for email)
- Services decoupled (Notification down doesn't affect order creation)
- Can retry email if SendGrid fails

---

### Example 3: Product Search (Cached)

```
┌─────────┐     ┌─────────┐     ┌──────────┐     ┌─────────┐
│ Browser │────►│ Web App │────►│ Gateway  │────►│ Catalog │
│         │     │ (React) │     │  (YARP)  │     │ Service │
└─────────┘     └─────────┘     └──────────┘     └────┬────┘
                                                       │
     ▲                                                 │
     │                                        ┌────────┴────────┐
     │                                        │                 │
     │                                        ▼                 ▼
     │                                  ┌──────────┐     ┌────────────┐
     │                                  │   Redis  │     │ PostgreSQL │
     │                                  │  (cache) │     │  (products)│
     │                                  └────┬─────┘     └────────────┘
     │                                       │
     │                                       │ Cache hit (90% of requests)
     │                                       │
     │◄──────────────────────────────────────┘
     │
     │ Response: { products: [...], totalCount: 1500 }
```

**Step-by-step**:

1. **GET /api/v1/products?page=1&pageSize=20&category=electronics**
2. **Catalog Service**:
   - Checks Redis: `products:page=1:pageSize=20:category=electronics`
   - **Cache hit** (90% of requests): Returns cached data immediately
   - **Cache miss** (10% of requests):
     - Queries PostgreSQL
     - Caches result in Redis (TTL: 10 minutes)
     - Returns data
3. **Response** returned to client

**Performance**:
- **Cache hit**: ~10ms (Redis lookup)
- **Cache miss**: ~150ms (PostgreSQL query + Redis set)

**Cache Invalidation**:
- Product updated → Invalidate cache for that product
- Product created → Invalidate category cache
- TTL expires → Automatic refresh

---

## Event Flow Diagrams

### Order Placement Flow (Saga Pattern)

```
┌──────────┐  BasketCheckedOutEvent  ┌──────────┐
│  Basket  │───────────────────────►│ Ordering │
│ Service  │                         │ Service  │
└──────────┘                         └────┬─────┘
                                          │
                                          │ OrderCreatedEvent
                                          ▼
                                   ┌─────────────┐
                    ┌──────────────│  RabbitMQ   │──────────────┐
                    │              │  (EventBus) │              │
                    │              └─────────────┘              │
                    │                                           │
                    ▼                                           ▼
             ┌────────────┐                              ┌────────────┐
             │  Payment   │                              │Notification│
             │  Service   │                              │  Service   │
             └─────┬──────┘                              └────────────┘
                   │                                            │
                   │ PaymentSuccessEvent                        │
                   │ (or PaymentFailedEvent)                    │
                   ▼                                            │
            ┌─────────────┐                                     │
            │  RabbitMQ   │                                     │
            └──────┬──────┘                                     │
                   │                                            │
                   │                                            │
                   ▼                                            ▼
            ┌──────────┐                                  ┌──────────┐
            │ Ordering │──────────────────────────────────│  Email   │
            │ Service  │  Update order status             │  Sent    │
            └──────────┘                                  └──────────┘
```

**States**:

1. **Basket Checkout**: User clicks "Place Order"
2. **Order Created**: Order in "Pending" state
3. **Payment Processing**: Payment Service charges card
4. **Payment Success/Failure**:
   - ✅ Success → Order status = "Paid"
   - ❌ Failure → Order status = "Cancelled" (saga compensation)
5. **Notifications**:
   - Email: "Order Confirmed"
   - Email: "Payment Receipt"

**Compensation** (if payment fails):
```csharp
// Ordering Service receives PaymentFailedEvent
public class PaymentFailedEventHandler : IConsumer<PaymentFailedEvent>
{
    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var order = await _repository.GetByIdAsync(context.Message.OrderId);
        
        // Compensate - mark order as cancelled
        order.Cancel("Payment failed");
        
        await _repository.SaveChangesAsync();
        
        // Publish OrderCancelledEvent (for notification service)
        await context.Publish(new OrderCancelledEvent(order.Id));
    }
}
```

---

## Communication Matrix

| Source Service | Target Service | Method | Pattern | Example |
|----------------|----------------|--------|---------|---------|
| **Web App** | API Gateway | HTTP | Sync | All requests |
| **API Gateway** | All Services | HTTP | Sync | Routing |
| **Basket** | Catalog | HTTP | Sync | Validate product exists |
| **Basket** | Ordering | Event | Async | BasketCheckedOutEvent |
| **Ordering** | Payment | Event | Async | ProcessPaymentCommand |
| **Ordering** | Notification | Event | Async | OrderCreatedEvent |
| **Payment** | Ordering | Event | Async | PaymentSuccessEvent |
| **Payment** | Stripe | HTTP | Sync | Process payment |
| **Notification** | SendGrid | HTTP | Sync | Send email |
| **Catalog** | Redis | In-memory | Cache | Product data |
| **All Services** | PostgreSQL | SQL | Sync | Data persistence |

---

## Data Consistency Patterns

### 1. Strong Consistency (ACID)

**Use case**: Within a single service (Order creation).

```csharp
using var transaction = await _context.Database.BeginTransactionAsync();

try
{
    // Create order
    var order = Order.Create(...);
    _context.Orders.Add(order);
    
    // Create order items
    foreach (var item in basketItems)
    {
        var orderItem = OrderItem.Create(order.Id, item.ProductId, item.Quantity);
        _context.OrderItems.Add(orderItem);
    }
    
    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

**Guarantees**: All-or-nothing (ACID).

---

### 2. Eventual Consistency (Events)

**Use case**: Across services (Order → Email notification).

```csharp
// Ordering Service
var order = Order.Create(...);
await _repository.SaveAsync(order); // ✅ Committed

// Publish event (async)
await _bus.Publish(new OrderCreatedEvent(order.Id));
// ❌ Email might be sent 2-5 seconds later
// ❌ Email might fail (but order is still created)
```

**Guarantees**: Eventually consistent (order exists, email will be sent eventually).

**Handling failures**:
- Retry logic (MassTransit retries 3 times)
- Dead-letter queue (manual intervention)
- Idempotency (handle duplicate events)

---

### 3. Saga Pattern (Distributed Transactions)

**Use case**: Multi-step workflows with compensation.

```
Order Created → Payment Processed → Inventory Reserved → Order Shipped
     ↓ (fail)         ↓ (fail)           ↓ (fail)
Cancel Order   ← Refund Payment   ← Release Inventory
```

**Implementation**: MassTransit State Machine (see Design Patterns doc).

---

## Data Duplication Strategy

**Problem**: Services have separate databases (Database-per-Service pattern).

**Example**: Product name stored in:
- **Catalog Service**: Full product data
- **Ordering Service**: Product name (snapshot at order time)
- **Basket Service**: Product name + price (ephemeral)

**Why**:
- Ordering Service can't join with Catalog database
- Order shows product name even if product is deleted

**Synchronization**:
- **At order creation**: Copy product data from Catalog
- **If product updated**: Old orders keep old data (historical snapshot)
- **If product deleted**: Orders still show product name

**Trade-off**:
- ✅ Independence (Ordering doesn't depend on Catalog at runtime)
- ✅ Historical data (order shows price at purchase time)
- ❌ Data duplication
- ❌ Eventual consistency

---

## Network Resilience

### Retry Policy

```csharp
// Basket Service calls Catalog Service to validate product

var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        onRetry: (exception, timespan, attempt, context) =>
        {
            _logger.LogWarning($"Retry {attempt} after {timespan.TotalSeconds}s");
        });

var product = await retryPolicy.ExecuteAsync(async () =>
{
    var response = await _httpClient.GetAsync($"/api/v1/products/{productId}");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<Product>();
});
```

**Retries**:
- Attempt 1: Immediate
- Attempt 2: Wait 2s
- Attempt 3: Wait 4s
- Attempt 4: Wait 8s
- Fail → Return error to user

---

### Circuit Breaker

```csharp
var circuitBreakerPolicy = Policy
    .Handle<HttpRequestException>()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30),
        onBreak: (exception, duration) =>
        {
            _logger.LogError("Circuit breaker opened for 30s");
        },
        onReset: () =>
        {
            _logger.LogInformation("Circuit breaker closed");
        });
```

**States**:
- **Closed**: Normal (all requests go through)
- **Open**: Service down (fail fast, don't make requests)
- **Half-Open**: Try 1 request to test if service recovered

---

## Monitoring Data Flow

### Distributed Tracing (Jaeger)

Every request gets a **Correlation ID**:

```
Browser → Gateway (X-Correlation-ID: abc123)
  ↓
Gateway → Catalog (X-Correlation-ID: abc123)
  ↓
Catalog → PostgreSQL (X-Correlation-ID: abc123)
  ↓
Catalog → Redis (X-Correlation-ID: abc123)
```

**Jaeger UI** shows:
- Request duration: 250ms
  - Gateway: 5ms
  - Catalog Service: 200ms
    - PostgreSQL query: 150ms
    - Redis set: 10ms
  - Network: 45ms

---

## Summary

| Pattern | Use Case | Consistency | Performance | Complexity |
|---------|----------|-------------|-------------|------------|
| **Sync HTTP** | Client needs response | Strong | Medium | Low |
| **Async Events** | Fire-and-forget | Eventual | High | Medium |
| **Saga** | Multi-step workflow | Eventual | High | High |
| **Cache** | Read-heavy data | Eventual | Very High | Low |
| **CQRS** | Different read/write needs | Eventual | Very High | Medium |

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
