# 📨 Message Broker (RabbitMQ + MassTransit)

Event-driven communication між мікросервісами з підтримкою RabbitMQ та MassTransit.

---

## Огляд

Message Broker забезпечує:
- ✅ **Async Communication** - Decoupling між сервісами
- ✅ **Event Publishing** - Domain events (ProductCreated, OrderPlaced)
- ✅ **Saga Orchestration** - Long-running transactions
- ✅ **Retry & Error Handling** - Automatic retry with exponential backoff
- ✅ **Dead Letter Queues** - Failed message handling
- ✅ **Message Routing** - Topic-based and direct routing

---

## Architecture

```
┌──────────────────┐         ┌──────────────────┐
│  Basket Service  │         │  Order Service   │
│                  │         │                  │
│  Checkout() ────►│         │                  │
└────────┬─────────┘         └────────▲─────────┘
         │                            │
         │ Publish                    │ Consume
         │ BasketCheckedOutEvent      │
         │                            │
         ▼                            │
    ┌────────────────────────────────┴────┐
    │        RabbitMQ Exchange            │
    │  (Topic: basket.checkout.event)     │
    └────────────────────────────────┬────┘
                                     │
         ┌───────────────────────────┼──────────────┐
         │                           │              │
         ▼                           ▼              ▼
    ┌─────────┐              ┌──────────┐    ┌──────────┐
    │ Order   │              │ Payment  │    │ Notif.   │
    │ Service │              │ Service  │    │ Service  │
    └─────────┘              └──────────┘    └──────────┘
```

---

## RabbitMQ Setup

### Docker Compose

```yaml
# deploy/docker/docker-compose.yml

services:
  rabbitmq:
    image: rabbitmq:3-management-alpine
    container_name: eshop-rabbitmq
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
      RABBITMQ_DEFAULT_VHOST: /
    ports:
      - "5672:5672"    # AMQP port
      - "15672:15672"  # Management UI
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
      - ./rabbitmq.conf:/etc/rabbitmq/rabbitmq.conf
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - eshop-network

volumes:
  rabbitmq_data:

networks:
  eshop-network:
    driver: bridge
```

**Access Management UI**: http://localhost:15672 (guest/guest)

---

### RabbitMQ Configuration

```conf
# rabbitmq.conf

# Memory limits
vm_memory_high_watermark.relative = 0.6

# Disk space
disk_free_limit.absolute = 2GB

# Heartbeat
heartbeat = 60

# Max message size
max_message_size = 134217728  # 128MB
```

---

## MassTransit Configuration

### NuGet Packages

```xml
<ItemGroup>
  <PackageReference Include="MassTransit" Version="8.1.0" />
  <PackageReference Include="MassTransit.RabbitMQ" Version="8.1.0" />
  <PackageReference Include="MassTransit.AspNetCore" Version="8.1.0" />
</ItemGroup>
```

---

### Program.cs Setup

```csharp
// Basket Service (Publisher)

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(x =>
{
    // Register consumers (if any)
    x.AddConsumer<ProductPriceChangedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetValue<string>("RabbitMQ:Host") ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration.GetValue<string>("RabbitMQ:Username") ?? "guest");
            h.Password(builder.Configuration.GetValue<string>("RabbitMQ:Password") ?? "guest");
        });

        // Configure exchanges and queues
        cfg.Message<BasketCheckedOutEvent>(e => e.SetEntityName("basket.checkout.event"));

        // Retry configuration
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

        // Configure endpoints
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.Run();
```

---

## Event Publishing

### 1. Define Event

```csharp
// EShop.BuildingBlocks.Messaging/Events/BasketCheckedOutEvent.cs

public record BasketCheckedOutEvent
{
    public string UserId { get; init; }
    public List<CheckoutItem> Items { get; init; }
    public decimal TotalPrice { get; init; }
    public string ShippingAddress { get; init; }
    public string PaymentMethod { get; init; }
    public DateTime CheckedOutAt { get; init; } = DateTime.UtcNow;
}

public record CheckoutItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; }
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}
```

---

### 2. Publish Event

```csharp
// EShop.Basket.Application/Commands/CheckoutBasket/CheckoutBasketHandler.cs

public class CheckoutBasketHandler : IRequestHandler<CheckoutBasketCommand, Guid>
{
    private readonly IBasketRepository _basketRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CheckoutBasketHandler> _logger;

    public async Task<Guid> Handle(
        CheckoutBasketCommand request,
        CancellationToken cancellationToken)
    {
        var basket = await _basketRepository.GetBasketAsync(request.UserId);
        
        if (basket is null)
            throw new NotFoundException($"Basket not found for user {request.UserId}");

        // Create event
        var checkoutEvent = new BasketCheckedOutEvent
        {
            UserId = basket.UserId,
            Items = basket.Items.Select(i => new CheckoutItem
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Price = i.Price,
                Quantity = i.Quantity
            }).ToList(),
            TotalPrice = basket.TotalPrice,
            ShippingAddress = request.ShippingAddress,
            PaymentMethod = request.PaymentMethod
        };

        // Publish to RabbitMQ
        await _publishEndpoint.Publish(checkoutEvent, cancellationToken);

        _logger.LogInformation(
            "BasketCheckedOutEvent published for user {UserId}", 
            request.UserId);

        // Clear basket
        await _basketRepository.DeleteBasketAsync(request.UserId);

        return Guid.NewGuid(); // OrderId (буде створений в Ordering Service)
    }
}
```

---

## Event Consumption

### 1. Create Consumer

```csharp
// EShop.Ordering.Application/Consumers/BasketCheckedOutConsumer.cs

public class BasketCheckedOutConsumer : IConsumer<BasketCheckedOutEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<BasketCheckedOutConsumer> _logger;

    public BasketCheckedOutConsumer(
        IMediator mediator,
        ILogger<BasketCheckedOutConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BasketCheckedOutEvent> context)
    {
        _logger.LogInformation(
            "Received BasketCheckedOutEvent for user {UserId}", 
            context.Message.UserId);

        try
        {
            var command = new CreateOrderCommand
            {
                UserId = context.Message.UserId,
                Items = context.Message.Items.Select(i => new CreateOrderItem
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Price = i.Price,
                    Quantity = i.Quantity
                }).ToList(),
                ShippingAddress = context.Message.ShippingAddress,
                PaymentMethod = context.Message.PaymentMethod
            };

            var orderId = await _mediator.Send(command);

            _logger.LogInformation("Order {OrderId} created from basket", orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing BasketCheckedOutEvent");
            throw; // MassTransit will retry
        }
    }
}
```

---

### 2. Register Consumer

```csharp
// Program.cs in Ordering Service

builder.Services.AddMassTransit(x =>
{
    // Register consumer
    x.AddConsumer<BasketCheckedOutConsumer>();
    x.AddConsumer<PaymentSuccessConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");

        // Configure consumer endpoint
        cfg.ReceiveEndpoint("order-service-basket-checkout", e =>
        {
            e.ConfigureConsumer<BasketCheckedOutConsumer>(context);
            
            // Retry policy
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            
            // Circuit breaker
            e.UseCircuitBreaker(cb =>
            {
                cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                cb.TripThreshold = 15;
                cb.ActiveThreshold = 10;
                cb.ResetInterval = TimeSpan.FromMinutes(5);
            });
        });

        cfg.ConfigureEndpoints(context);
    });
});
```

---

## Advanced Patterns

### 1. Request-Response Pattern

**Request:**
```csharp
// Publisher
var response = await _requestClient.GetResponse<ProductStockResponse>(
    new CheckProductStockRequest 
    { 
        ProductId = productId,
        RequestedQuantity = quantity
    });

if (response.Message.IsAvailable)
{
    // Process order
}
```

**Handler:**
```csharp
// Consumer in Catalog Service
public class CheckProductStockConsumer : IConsumer<CheckProductStockRequest>
{
    public async Task Consume(ConsumeContext<CheckProductStockRequest> context)
    {
        var product = await _repository.GetByIdAsync(context.Message.ProductId);
        
        await context.RespondAsync(new ProductStockResponse
        {
            ProductId = product.Id,
            IsAvailable = product.StockQuantity >= context.Message.RequestedQuantity,
            AvailableQuantity = product.StockQuantity
        });
    }
}
```

---

### 2. Saga Pattern (Long-Running Transactions)

```csharp
// EShop.Ordering.Application/Sagas/OrderStateMachine.cs

public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderCreated, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentSuccess, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));

        Initially(
            When(OrderCreated)
                .Then(context =>
                {
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.UserId = context.Message.UserId;
                    context.Saga.TotalAmount = context.Message.TotalAmount;
                })
                .Publish(context => new ProcessPaymentCommand
                {
                    OrderId = context.Saga.OrderId,
                    Amount = context.Saga.TotalAmount
                })
                .TransitionTo(WaitingForPayment)
        );

        During(WaitingForPayment,
            When(PaymentSuccess)
                .Publish(context => new OrderPaidEvent
                {
                    OrderId = context.Saga.OrderId
                })
                .TransitionTo(Paid),

            When(PaymentFailed)
                .Publish(context => new CancelOrderCommand
                {
                    OrderId = context.Saga.OrderId,
                    Reason = "Payment failed"
                })
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

public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; }
    public Guid OrderId { get; set; }
    public string UserId { get; set; }
    public decimal TotalAmount { get; set; }
}
```

**Register Saga:**
```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .InMemoryRepository(); // Use EntityFramework in production

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");
        cfg.ConfigureEndpoints(context);
    });
});
```

---

### 3. Outbox Pattern (Transactional Messaging)

```csharp
// Save domain events to Outbox table in same transaction

public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var domainEvents = ChangeTracker
        .Entries<AggregateRoot>()
        .SelectMany(x => x.Entity.DomainEvents)
        .ToList();

    // Save events to Outbox
    foreach (var domainEvent in domainEvents)
    {
        OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = domainEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(domainEvent),
            OccurredOn = DateTime.UtcNow
        });
    }

    return await base.SaveChangesAsync(ct);
}
```

**Background Worker publishes from Outbox:**
```csharp
public class OutboxProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var messages = await GetUnprocessedMessagesAsync();

            foreach (var message in messages)
            {
                try
                {
                    var @event = DeserializeEvent(message);
                    await _publishEndpoint.Publish(@event);
                    await MarkAsProcessedAsync(message.Id);
                }
                catch (Exception ex)
                {
                    await MarkAsFailedAsync(message.Id, ex.Message);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

---

## Error Handling

### Retry Policy

```csharp
cfg.UseMessageRetry(r =>
{
    r.Exponential(
        retryLimit: 5,
        minInterval: TimeSpan.FromSeconds(1),
        maxInterval: TimeSpan.FromMinutes(5),
        intervalDelta: TimeSpan.FromSeconds(2)
    );
    
    // Ignore specific exceptions
    r.Ignore<ValidationException>();
    r.Ignore<NotFoundException>();
});
```

---

### Dead Letter Queue (DLQ)

```csharp
cfg.ReceiveEndpoint("order-service-basket-checkout", e =>
{
    e.ConfigureConsumer<BasketCheckedOutConsumer>(context);
    
    // After retries exhausted, move to DLQ
    e.UseMessageRetry(r => r.Interval(3, 5));
    
    // Dead letter exchange
    e.BindDeadLetterQueue("order-service-basket-checkout-error");
});
```

**Manually process DLQ:**
```bash
# View messages in DLQ via Management UI
# http://localhost:15672/#/queues/%2F/order-service-basket-checkout-error
```

---

## Monitoring

### RabbitMQ Management UI

http://localhost:15672

**Key Metrics:**
- Message rate (publish/consume)
- Queue depth
- Consumer count
- Unacked messages

---

### MassTransit Diagnostics

```csharp
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("localhost");
        
        // Enable diagnostics
        cfg.UseHealthCheck(context);
        
        cfg.ConfigureEndpoints(context);
    });
});

// Add health check endpoint
builder.Services.AddHealthChecks()
    .AddRabbitMQ(rabbitConnectionString: "amqp://localhost");
```

---

## Performance Tips

### 1. Prefetch Count

```csharp
e.PrefetchCount = 16; // Process up to 16 messages concurrently
```

### 2. Message Serialization

MassTransit uses JSON by default. For better performance:

```csharp
cfg.ConfigureJsonSerializerOptions(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    return options;
});
```

### 3. Publisher Confirms

```csharp
cfg.PublisherConfirmation = true; // Ensure messages are persisted
```

---

## Security

### TLS/SSL

```csharp
cfg.Host("rabbitmq.example.com", h =>
{
    h.Username("user");
    h.Password("password");
    h.UseSsl(s =>
    {
        s.ServerName = "rabbitmq.example.com";
        s.Protocol = SslProtocols.Tls12;
    });
});
```

### Virtual Hosts

```csharp
cfg.Host("localhost", "/production", h =>
{
    h.Username("prod-user");
    h.Password("secure-password");
});
```

---

## Testing

### In-Memory Test Harness

```csharp
[Fact]
public async Task Should_Consume_BasketCheckedOutEvent()
{
    await using var provider = new ServiceCollection()
        .AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<BasketCheckedOutConsumer>();
        })
        .BuildServiceProvider(true);

    var harness = provider.GetRequiredService<ITestHarness>();
    await harness.Start();

    // Publish event
    await harness.Bus.Publish(new BasketCheckedOutEvent
    {
        UserId = "user123",
        TotalPrice = 100m
    });

    // Assert consumed
    Assert.True(await harness.Consumed.Any<BasketCheckedOutEvent>());
}
```

---

## Best Practices

### ✅ DO

1. **Use Idempotent Consumers** - Handle duplicate messages
2. **Set Retry Limits** - Avoid infinite loops
3. **Monitor Queue Depth** - Alert on backlog
4. **Use Correlation IDs** - For request tracing
5. **Implement Outbox Pattern** - For transactional messaging
6. **Version Events** - Add version field for compatibility

### ❌ DON'T

1. **Don't Block Consumers** - Use async/await
2. **Don't Store Large Payloads** - Use references instead
3. **Don't Ignore Errors** - Log and handle properly
4. **Don't Auto-Delete Queues** - In production
5. **Don't Expose RabbitMQ Publicly** - Use internal network

---

## Наступні кроки

- ✅ [Observability](observability.md) - Tracing message flows
- ✅ [Resilience](resilience.md) - Circuit breakers
- ✅ [Testing Strategy](../../08-testing/testing-strategy.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
