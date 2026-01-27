# 🎨 Design Patterns Used

Каталог design patterns, які використовуються в проєкті.

---

## Domain-Driven Design Patterns

### 1. Aggregate Pattern

**Purpose**: Group related objects into a consistency boundary.

**Implementation**:

```csharp
// Product is the Aggregate Root
public class Product : AggregateRoot
{
    // Private collection - can only be modified through domain methods
    private readonly List<ProductImage> _images = new();
    
    public IReadOnlyList<ProductImage> Images => _images.AsReadOnly();
    
    // All modifications go through the aggregate root
    public void AddImage(string url, string altText, int displayOrder)
    {
        var image = new ProductImage
        {
            Id = Guid.NewGuid(),
            Url = url,
            AltText = altText,
            DisplayOrder = displayOrder
        };
        
        _images.Add(image);
        AddDomainEvent(new ProductImageAddedEvent(Id, image.Id));
    }
    
    // Cannot add images directly - must go through aggregate root
    // _images.Add(new ProductImage()) ❌ Not allowed
}
```

**Used in**:
- `Product` (Catalog Service)
- `Order` (Ordering Service)
- `ShoppingBasket` (Basket Service)

**Benefits**:
- Enforces invariants
- Clear transactional boundary
- Single source of truth

---

### 2. Value Object Pattern

**Purpose**: Objects defined by their values, not identity.

**Implementation**:

```csharp
public record Money
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    
    public Money(decimal amount, string currency = "USD")
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative");
        
        Amount = amount;
        Currency = currency;
    }
    
    // Equality based on values
    public virtual bool Equals(Money? other)
    {
        return other != null && 
               Amount == other.Amount && 
               Currency == other.Currency;
    }
    
    // Immutable operations
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add different currencies");
        
        return new Money(Amount + other.Amount, Currency);
    }
}

// Usage
var price1 = new Money(99.99m);
var price2 = new Money(99.99m);
price1 == price2; // ✅ true (value equality)
```

**Used in**:
- `Money` (Price representation)
- `Email` (Email address validation)
- `Address` (Shipping address)
- `RefreshToken` (Token data)

**Benefits**:
- Immutable
- Self-validating
- Expressive domain model

---

### 3. Domain Events Pattern

**Purpose**: Decouple domain logic from side effects.

**Implementation**:

```csharp
// Domain Event
public record ProductCreatedEvent(Guid ProductId, string ProductName) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

// Aggregate raises event
public class Product : AggregateRoot
{
    public static Product Create(string name, ...)
    {
        var product = new Product { ... };
        
        // Raise domain event
        product.AddDomainEvent(new ProductCreatedEvent(product.Id, product.Name));
        
        return product;
    }
}

// Event handler
public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    public async Task Handle(ProductCreatedEvent @event, CancellationToken ct)
    {
        // Publish integration event to RabbitMQ
        await _bus.Publish(new ProductCreatedIntegrationEvent(@event.ProductId));
        
        // Update read model (if using CQRS)
        // Send notification
    }
}
```

**Used in**:
- Product created/updated
- Order placed/cancelled
- Payment succeeded/failed

**Benefits**:
- Decoupled side effects
- Audit trail
- Asynchronous processing

---

## Application Patterns

### 4. CQRS Pattern

**Purpose**: Separate read and write operations.

**Implementation**:

```csharp
// ===== WRITE SIDE (Commands) =====

public record CreateProductCommand : IRequest<Result<Guid>>
{
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    // ... other properties
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        // Complex domain logic
        var product = Product.Create(request.Name, new Money(request.Price), ...);
        
        await _repository.AddAsync(product, ct);
        await _repository.SaveChangesAsync(ct);
        
        return Result<Guid>.Success(product.Id);
    }
}

// ===== READ SIDE (Queries) =====

public record GetProductsQuery : IRequest<PagedResult<ProductDto>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        // Simple data fetching (no domain logic)
        var products = await _context.Products
            .AsNoTracking() // Read-only
            .Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price.Amount
            })
            .ToPagedListAsync(request.Page, request.PageSize, ct);
        
        return products;
    }
}
```

**Benefits**:
- Optimized reads (no domain logic overhead)
- Scalable (can cache queries aggressively)
- Clear separation of concerns

---

### 5. Mediator Pattern (MediatR)

**Purpose**: Reduce coupling between components.

**Implementation**:

```csharp
// Controller doesn't know about handlers
[ApiController]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;
    
    [HttpPost]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductCommand command)
    {
        var result = await _mediator.Send(command);
        
        return result.IsSuccess 
            ? CreatedAtAction(nameof(GetProduct), new { id = result.Value }, result.Value)
            : BadRequest(result.Error);
    }
}

// MediatR finds and invokes the handler
// No tight coupling between controller and handler
```

**Benefits**:
- Loose coupling
- Easy to add behaviors (logging, validation)
- Testability

---

### 6. Repository Pattern

**Purpose**: Abstract data access logic.

**Implementation**:

```csharp
// Interface (in Domain layer)
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Product>> GetProductsAsync(int page, int pageSize, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
    Task UpdateAsync(Product product, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

// Implementation (in Infrastructure layer)
public class ProductRepository : IProductRepository
{
    private readonly CatalogDbContext _context;
    
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }
    
    // Other methods...
}
```

**Benefits**:
- Testability (can mock repository)
- Can swap data source (EF Core → Dapper)
- Hides EF Core details from domain

---

## Infrastructure Patterns

### 7. Unit of Work Pattern

**Purpose**: Coordinate multiple repository operations in a single transaction.

**Implementation**:

```csharp
public class CatalogDbContext : DbContext, IUnitOfWork
{
    public DbSet<Product> Products { get; set; }
    public DbSet<Category> Categories { get; set; }
    
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Dispatch domain events before saving
        var events = ChangeTracker.Entries<AggregateRoot>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();
        
        foreach (var @event in events)
        {
            await _mediator.Publish(@event, ct);
        }
        
        // Clear events
        foreach (var entity in ChangeTracker.Entries<AggregateRoot>())
        {
            entity.Entity.ClearDomainEvents();
        }
        
        // Save changes
        return await base.SaveChangesAsync(ct);
    }
}
```

**Benefits**:
- Atomic transactions
- Automatic event dispatching
- Single save point

---

### 8. Decorator Pattern (Caching)

**Purpose**: Add caching behavior without modifying repository.

**Implementation**:

```csharp
public class CachedProductRepository : IProductRepository
{
    private readonly IProductRepository _inner;
    private readonly IDistributedCache _cache;
    
    public CachedProductRepository(IProductRepository inner, IDistributedCache cache)
    {
        _inner = inner;
        _cache = cache;
    }
    
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cacheKey = $"product:{id}";
        
        // Try cache first
        var cached = await _cache.GetAsync<Product>(cacheKey, ct);
        if (cached != null)
            return cached;
        
        // Fallback to database
        var product = await _inner.GetByIdAsync(id, ct);
        
        // Cache result
        if (product != null)
        {
            await _cache.SetAsync(cacheKey, product, TimeSpan.FromMinutes(10), ct);
        }
        
        return product;
    }
}

// Registration
services.AddScoped<IProductRepository, ProductRepository>();
services.Decorate<IProductRepository, CachedProductRepository>();
```

**Benefits**:
- Separation of concerns
- Can add/remove caching without changing code
- Multiple decorators (logging, caching, retries)

---

### 9. Circuit Breaker Pattern (Polly)

**Purpose**: Prevent cascading failures.

**Implementation**:

```csharp
// HTTP Client with Circuit Breaker
builder.Services.AddHttpClient<IProductApiClient, ProductApiClient>()
    .AddPolicyHandler(GetCircuitBreakerPolicy());

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, duration) =>
            {
                Console.WriteLine($"Circuit breaker opened for {duration.TotalSeconds}s");
            },
            onReset: () =>
            {
                Console.WriteLine("Circuit breaker closed");
            });
}
```

**States**:
- **Closed**: Normal operation
- **Open**: Service is down, fail fast (don't make requests)
- **Half-Open**: Try one request to test if service recovered

**Benefits**:
- Prevent resource exhaustion
- Fail fast
- Automatic recovery

---

### 10. Retry Pattern (Polly)

**Purpose**: Handle transient failures.

**Implementation**:

```csharp
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Console.WriteLine($"Retry {retryAttempt} after {timespan.TotalSeconds}s");
            });
}
```

**Exponential Backoff**:
- Retry 1: Wait 2s
- Retry 2: Wait 4s
- Retry 3: Wait 8s

**Benefits**:
- Automatic retry on transient errors (network timeouts, 503 errors)
- Exponential backoff prevents overwhelming failing service

---

## Messaging Patterns

### 11. Outbox Pattern

**Purpose**: Ensure reliable event publishing.

**Implementation**:

```csharp
// Save event to database in same transaction
public class OrderCreatedEventHandler : INotificationHandler<OrderCreatedEvent>
{
    private readonly OrderDbContext _context;
    
    public async Task Handle(OrderCreatedEvent @event, CancellationToken ct)
    {
        // Store event in outbox table (same transaction as order creation)
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = @event.GetType().Name,
            Payload = JsonSerializer.Serialize(@event),
            CreatedAt = DateTime.UtcNow
        };
        
        _context.OutboxMessages.Add(outboxMessage);
        await _context.SaveChangesAsync(ct);
    }
}

// Background job publishes events from outbox
public class OutboxPublisher : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var messages = await _context.OutboxMessages
                .Where(m => m.ProcessedAt == null)
                .Take(100)
                .ToListAsync(ct);
            
            foreach (var message in messages)
            {
                try
                {
                    // Publish to RabbitMQ
                    await _bus.Publish(DeserializeEvent(message), ct);
                    
                    message.ProcessedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    message.FailureCount++;
                    message.LastError = ex.Message;
                }
            }
            
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
}
```

**Benefits**:
- At-least-once delivery guarantee
- No lost events (even if RabbitMQ is down)
- Transactional consistency

---

### 12. Saga Pattern

**Purpose**: Manage distributed transactions.

**Implementation** (MassTransit State Machine):

```csharp
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);
        
        Initially(
            When(OrderCreated)
                .Then(context => Console.WriteLine($"Order {context.Saga.OrderId} created"))
                .Publish(context => new ProcessPaymentCommand
                {
                    OrderId = context.Saga.OrderId,
                    Amount = context.Message.TotalAmount
                })
                .TransitionTo(WaitingForPayment)
        );
        
        During(WaitingForPayment,
            When(PaymentSuccess)
                .Then(context => Console.WriteLine($"Payment succeeded for order {context.Saga.OrderId}"))
                .TransitionTo(Paid),
                
            When(PaymentFailed)
                .Then(context => Console.WriteLine($"Payment failed, cancelling order"))
                .Publish(context => new CancelOrderCommand { OrderId = context.Saga.OrderId })
                .TransitionTo(Cancelled)
        );
    }
    
    public State WaitingForPayment { get; private set; }
    public State Paid { get; private set; }
    public State Cancelled { get; private set; }
    
    public Event<OrderCreatedEvent> OrderCreated { get; private set; }
    public Event<PaymentSuccessEvent> PaymentSuccess { get; private set; }
    public Event<PaymentFailedEvent> PaymentFailed { get; private set; }
}
```

**Benefits**:
- Handles complex distributed workflows
- Automatic compensation on failures
- State persistence

---

## API Patterns

### 13. API Gateway Pattern

**Purpose**: Single entry point for all clients.

**Implementation** (YARP):

```json
{
  "ReverseProxy": {
    "Routes": {
      "catalog-route": {
        "ClusterId": "catalog-cluster",
        "Match": {
          "Path": "/api/catalog/{**catch-all}"
        },
        "Transforms": [
          { "PathPattern": "/api/v1/{**catch-all}" }
        ]
      }
    },
    "Clusters": {
      "catalog-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://catalog-service"
          }
        }
      }
    }
  }
}
```

**Responsibilities**:
- Routing
- Authentication
- Rate limiting
- Response caching
- Request/response transformation

---

## Summary Table

| Pattern | Category | Used In | Purpose |
|---------|----------|---------|---------|
| Aggregate | DDD | Product, Order | Consistency boundary |
| Value Object | DDD | Money, Email | Immutable values |
| Domain Events | DDD | All services | Decouple side effects |
| CQRS | Application | All services | Separate reads/writes |
| Mediator | Application | All services | Decouple handlers |
| Repository | Infrastructure | All services | Abstract data access |
| Unit of Work | Infrastructure | All services | Transaction coordination |
| Decorator | Infrastructure | Caching | Add behavior |
| Circuit Breaker | Resilience | HTTP clients | Prevent cascading failures |
| Retry | Resilience | HTTP clients | Handle transient errors |
| Outbox | Messaging | Ordering | Reliable event publishing |
| Saga | Messaging | Order workflow | Distributed transactions |
| API Gateway | API | YARP | Single entry point |

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
