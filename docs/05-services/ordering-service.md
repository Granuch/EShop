# 📦 Ordering Service

Сервіс управління замовленнями з підтримкою DDD, Saga patterns та event sourcing.

---

## Огляд

Ordering Service відповідає за:
- ✅ Створення замовлень (consume BasketCheckedOutEvent)
- ✅ Управління lifecycle замовлення (OrderAggregate з DDD)
- ✅ Order Saga pattern (координація з Payment і Notification)
- ✅ Order history та статуси
- ✅ Cancellation та Refunds
- ✅ Publishing events (OrderCreated, OrderPaid, OrderShipped)

---

## Архітектура

### Технології

| Компонент | Технологія | Призначення |
|-----------|------------|-------------|
| **Framework** | ASP.NET Core 9.0 | Web API |
| **Database** | PostgreSQL | Order storage |
| **Message Bus** | RabbitMQ + MassTransit | Event-driven communication |
| **CQRS** | MediatR | Command/Query separation |
| **DDD** | Aggregates, Value Objects | Domain modeling |
| **Outbox Pattern** | EF Core | Transactional messaging |

### Domain Model (DDD)

```
EShop.Ordering.Domain/
├── Aggregates/
│   └── Order/
│       ├── Order.cs                # Aggregate Root
│       ├── OrderItem.cs            # Entity
│       └── OrderStatus.cs          # Enum
├── ValueObjects/
│   ├── Address.cs
│   ├── Money.cs
│   └── OrderId.cs
├── Events/
│   ├── OrderCreatedEvent.cs
│   ├── OrderPaidEvent.cs
│   ├── OrderShippedEvent.cs
│   └── OrderCancelledEvent.cs
└── Interfaces/
    └── IOrderRepository.cs
```

---

## Order Aggregate

```csharp
// EShop.Ordering.Domain/Aggregates/Order/Order.cs

public class Order : AggregateRoot<Guid>
{
    public string UserId { get; private set; }
    public Address ShippingAddress { get; private set; }
    public Money TotalPrice { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? PaymentIntentId { get; private set; }
    
    private readonly List<OrderItem> _items = [];
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    private Order() { }

    public static Order Create(
        string userId,
        Address shippingAddress,
        IEnumerable<OrderItem> items)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var item in items)
        {
            order._items.Add(item);
        }

        order.TotalPrice = new Money(order._items.Sum(i => i.SubTotal.Amount));
        order.AddDomainEvent(new OrderCreatedEvent(order.Id, userId, order.TotalPrice.Amount));

        return order;
    }

    public void MarkAsPaid(string paymentIntentId)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Only pending orders can be marked as paid");

        Status = OrderStatus.Paid;
        PaymentIntentId = paymentIntentId;
        PaidAt = DateTime.UtcNow;

        AddDomainEvent(new OrderPaidEvent(Id, paymentIntentId));
    }

    public void Ship()
    {
        if (Status != OrderStatus.Paid)
            throw new DomainException("Only paid orders can be shipped");

        Status = OrderStatus.Shipped;
        ShippedAt = DateTime.UtcNow;

        AddDomainEvent(new OrderShippedEvent(Id, UserId));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
            throw new DomainException("Cannot cancel shipped/delivered orders");

        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;

        AddDomainEvent(new OrderCancelledEvent(Id, reason));
    }
}

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Delivered,
    Cancelled,
    Refunded
}
```

---

## Address Value Object

```csharp
// EShop.Ordering.Domain/ValueObjects/Address.cs

public record Address
{
    public string Street { get; init; }
    public string City { get; init; }
    public string State { get; init; }
    public string ZipCode { get; init; }
    public string Country { get; init; }

    public Address(string street, string city, string state, string zipCode, string country)
    {
        Street = !string.IsNullOrWhiteSpace(street) 
            ? street 
            : throw new ArgumentException("Street is required");
        City = city;
        State = state;
        ZipCode = zipCode;
        Country = country;
    }

    public override string ToString() => $"{Street}, {City}, {State} {ZipCode}, {Country}";
}
```

---

## Event Consumers

### BasketCheckedOutConsumer

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
}
```

---

## API Endpoints

### GET /api/v1/orders

Отримати всі замовлення користувача.

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "status": "Shipped",
      "totalPrice": 2999.98,
      "itemsCount": 2,
      "createdAt": "2024-01-15T10:00:00Z",
      "shippedAt": "2024-01-16T14:00:00Z"
    }
  ]
}
```

---

### GET /api/v1/orders/{id}

Отримати деталі замовлення.

**Response:** `200 OK`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userId": "...",
  "status": "Paid",
  "items": [
    {
      "productId": "...",
      "productName": "Laptop Dell XPS 15",
      "price": 1499.99,
      "quantity": 2,
      "subTotal": 2999.98
    }
  ],
  "shippingAddress": {
    "street": "123 Main St",
    "city": "New York",
    "state": "NY",
    "zipCode": "10001",
    "country": "USA"
  },
  "totalPrice": 2999.98,
  "paymentIntentId": "pi_abc123",
  "createdAt": "2024-01-15T10:00:00Z",
  "paidAt": "2024-01-15T10:05:00Z"
}
```

---

### POST /api/v1/orders/{id}/cancel

Скасувати замовлення.

**Request:**
```json
{
  "reason": "Changed my mind"
}
```

**Response:** `204 No Content`

---

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ordering;Username=eshop;Password=eshop123"
  },
  
  "RabbitMQ": {
    "Host": "localhost"
  }
}
```

---

## Наступні кроки

- ✅ [Payment Service](payment-service.md) - Process payments
- ✅ [Notification Service](notification-service.md) - Send order confirmations
- ✅ [Message Broker Setup](../../06-infrastructure/message-broker.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
