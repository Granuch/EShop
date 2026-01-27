# 🛒 Basket Service

Сервіс управління кошиком покупок з підтримкою Redis та event-driven architecture.

---

## Огляд

Basket Service відповідає за:
- ✅ Додавання/видалення товарів з кошика
- ✅ Оновлення кількості товарів
- ✅ Зберігання кошика в Redis (швидкий доступ)
- ✅ Merge кошиків (анонімний + авторизований юзер)
- ✅ Checkout процес
- ✅ Publishing BasketCheckedOutEvent для Ordering Service
- ✅ Автоматичне видалення неактивних кошиків (TTL)

---

## Архітектура

### Технології

| Компонент | Технологія | Призначення |
|-----------|------------|-------------|
| **Framework** | ASP.NET Core 9.0 Minimal API | Web API |
| **Storage** | Redis | In-memory basket storage |
| **Message Bus** | RabbitMQ + MassTransit | Event publishing |
| **CQRS** | MediatR | Command/Query separation |
| **Validation** | FluentValidation | Input validation |

### Clean Architecture Layers

```
EShop.Basket.API/
├── Endpoints/
│   └── BasketEndpoints.cs         # GET, POST, DELETE /basket
├── Program.cs
└── appsettings.json

EShop.Basket.Domain/
├── Entities/
│   ├── ShoppingBasket.cs          # Aggregate Root
│   └── BasketItem.cs              # Entity
├── ValueObjects/
│   └── ProductInfo.cs
├── Interfaces/
│   └── IBasketRepository.cs
└── Events/
    ├── BasketCheckedOutEvent.cs
    └── BasketItemAddedEvent.cs

EShop.Basket.Application/
├── Basket/
│   ├── Commands/
│   │   ├── AddItemToBasket/
│   │   ├── UpdateBasketItem/
│   │   ├── RemoveItemFromBasket/
│   │   ├── CheckoutBasket/
│   │   └── MergeBaskets/
│   └── Queries/
│       ├── GetBasket/
│       └── GetBasketSummary/
└── Common/
    └── DTOs/
        ├── BasketDto.cs
        └── BasketItemDto.cs

EShop.Basket.Infrastructure/
├── Repositories/
│   └── RedisBasketRepository.cs   # Redis implementation
├── Services/
│   └── ProductPriceService.cs     # Validate product prices
└── Extensions/
    └── ServiceCollectionExtensions.cs
```

---

## Domain Entities

### ShoppingBasket (Aggregate Root)

```csharp
// EShop.Basket.Domain/Entities/ShoppingBasket.cs

public class ShoppingBasket : AggregateRoot<string>
{
    // ID = UserId (або Anonymous GUID)
    public string UserId { get; private set; }
    
    private readonly List<BasketItem> _items = [];
    public IReadOnlyCollection<BasketItem> Items => _items.AsReadOnly();
    
    public DateTime CreatedAt { get; private set; }
    public DateTime LastModifiedAt { get; private set; }
    
    public decimal TotalPrice => _items.Sum(i => i.SubTotal);
    public int TotalItems => _items.Sum(i => i.Quantity);

    private ShoppingBasket() { }

    public static ShoppingBasket Create(string userId)
    {
        return new ShoppingBasket
        {
            Id = userId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            LastModifiedAt = DateTime.UtcNow
        };
    }

    public void AddItem(Guid productId, string productName, decimal price, int quantity)
    {
        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);

        if (existingItem is not null)
        {
            existingItem.UpdateQuantity(existingItem.Quantity + quantity);
        }
        else
        {
            var item = new BasketItem(productId, productName, price, quantity);
            _items.Add(item);
        }

        LastModifiedAt = DateTime.UtcNow;
        AddDomainEvent(new BasketItemAddedEvent(UserId, productId, quantity));
    }

    public void UpdateItemQuantity(Guid productId, int newQuantity)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        
        if (item is null)
            throw new DomainException($"Product {productId} not found in basket");

        if (newQuantity <= 0)
        {
            _items.Remove(item);
        }
        else
        {
            item.UpdateQuantity(newQuantity);
        }

        LastModifiedAt = DateTime.UtcNow;
    }

    public void RemoveItem(Guid productId)
    {
        var item = _items.FirstOrDefault(i => i.ProductId == productId);
        if (item is not null)
        {
            _items.Remove(item);
            LastModifiedAt = DateTime.UtcNow;
        }
    }

    public void Clear()
    {
        _items.Clear();
        LastModifiedAt = DateTime.UtcNow;
    }

    public BasketCheckedOutEvent Checkout(string shippingAddress)
    {
        if (!_items.Any())
            throw new DomainException("Cannot checkout empty basket");

        return new BasketCheckedOutEvent
        {
            UserId = UserId,
            Items = _items.Select(i => new CheckoutItem
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Price = i.Price,
                Quantity = i.Quantity
            }).ToList(),
            TotalPrice = TotalPrice,
            ShippingAddress = shippingAddress
        };
    }
}
```

### BasketItem (Entity)

```csharp
// EShop.Basket.Domain/Entities/BasketItem.cs

public class BasketItem : Entity<Guid>
{
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; }
    public decimal Price { get; private set; }
    public int Quantity { get; private set; }
    public decimal SubTotal => Price * Quantity;

    private BasketItem() { }

    public BasketItem(Guid productId, string productName, decimal price, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        Id = Guid.NewGuid();
        ProductId = productId;
        ProductName = productName;
        Price = price;
        Quantity = quantity;
    }

    public void UpdateQuantity(int newQuantity)
    {
        if (newQuantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(newQuantity));

        Quantity = newQuantity;
    }

    public void UpdatePrice(decimal newPrice)
    {
        if (newPrice < 0)
            throw new ArgumentException("Price cannot be negative", nameof(newPrice));

        Price = newPrice;
    }
}
```

---

## API Endpoints

### GET /api/v1/basket/{userId}

Отримати кошик користувача.

**Response:** `200 OK`
```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "items": [
    {
      "productId": "...",
      "productName": "Laptop Dell XPS 15",
      "price": 1499.99,
      "quantity": 2,
      "subTotal": 2999.98
    }
  ],
  "totalPrice": 2999.98,
  "totalItems": 2,
  "createdAt": "2024-01-15T10:00:00Z",
  "lastModifiedAt": "2024-01-15T14:30:00Z"
}
```

---

### POST /api/v1/basket/{userId}/items

Додати товар до кошика.

**Request:**
```json
{
  "productId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "productName": "Laptop Dell XPS 15",
  "price": 1499.99,
  "quantity": 1
}
```

**Response:** `204 No Content`

---

### PUT /api/v1/basket/{userId}/items/{productId}

Оновити кількість товару.

**Request:**
```json
{
  "quantity": 3
}
```

**Response:** `204 No Content`

---

### DELETE /api/v1/basket/{userId}/items/{productId}

Видалити товар з кошика.

**Response:** `204 No Content`

---

### DELETE /api/v1/basket/{userId}

Очистити весь кошик.

**Response:** `204 No Content`

---

### POST /api/v1/basket/{userId}/checkout

Оформити замовлення (publish BasketCheckedOutEvent).

**Request:**
```json
{
  "shippingAddress": {
    "street": "123 Main St",
    "city": "New York",
    "state": "NY",
    "zipCode": "10001",
    "country": "USA"
  },
  "paymentMethod": "CreditCard"
}
```

**Response:** `202 Accepted`
```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "message": "Order is being processed"
}
```

---

## Implementation

### Redis Repository

```csharp
// EShop.Basket.Infrastructure/Repositories/RedisBasketRepository.cs

public class RedisBasketRepository : IBasketRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBasketRepository> _logger;
    private static readonly TimeSpan BasketTTL = TimeSpan.FromDays(30);

    public RedisBasketRepository(
        IConnectionMultiplexer redis,
        ILogger<RedisBasketRepository> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<ShoppingBasket?> GetBasketAsync(string userId)
    {
        var db = _redis.GetDatabase();
        var data = await db.StringGetAsync(GetKey(userId));

        if (data.IsNullOrEmpty)
        {
            _logger.LogDebug("Basket not found for user {UserId}", userId);
            return null;
        }

        return JsonSerializer.Deserialize<ShoppingBasket>(data!);
    }

    public async Task<ShoppingBasket> SaveBasketAsync(ShoppingBasket basket)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(basket);
        
        var success = await db.StringSetAsync(
            GetKey(basket.UserId),
            json,
            BasketTTL);

        if (!success)
        {
            _logger.LogError("Failed to save basket for user {UserId}", basket.UserId);
            throw new Exception("Failed to save basket");
        }

        _logger.LogInformation("Basket saved for user {UserId}", basket.UserId);
        return basket;
    }

    public async Task<bool> DeleteBasketAsync(string userId)
    {
        var db = _redis.GetDatabase();
        return await db.KeyDeleteAsync(GetKey(userId));
    }

    private static string GetKey(string userId) => $"basket:{userId}";
}
```

---

### Checkout Command

```csharp
// EShop.Basket.Application/Basket/Commands/CheckoutBasket/CheckoutBasketHandler.cs

public class CheckoutBasketHandler : IRequestHandler<CheckoutBasketCommand, Guid>
{
    private readonly IBasketRepository _basketRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CheckoutBasketHandler> _logger;

    public CheckoutBasketHandler(
        IBasketRepository basketRepository,
        IPublishEndpoint publishEndpoint,
        ILogger<CheckoutBasketHandler> logger)
    {
        _basketRepository = basketRepository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Guid> Handle(
        CheckoutBasketCommand request,
        CancellationToken cancellationToken)
    {
        var basket = await _basketRepository.GetBasketAsync(request.UserId);
        
        if (basket is null)
            throw new NotFoundException($"Basket not found for user {request.UserId}");

        // Create checkout event
        var checkoutEvent = basket.Checkout(request.ShippingAddress);
        checkoutEvent.PaymentMethod = request.PaymentMethod;

        // Publish to RabbitMQ
        await _publishEndpoint.Publish(checkoutEvent, cancellationToken);

        _logger.LogInformation(
            "BasketCheckedOutEvent published for user {UserId}", 
            request.UserId);

        // Clear basket after checkout
        await _basketRepository.DeleteBasketAsync(request.UserId);

        return Guid.NewGuid(); // OrderId (буде створений в Ordering Service)
    }
}
```

---

### BasketCheckedOutEvent

```csharp
// EShop.Basket.Domain/Events/BasketCheckedOutEvent.cs

public record BasketCheckedOutEvent
{
    public string UserId { get; init; }
    public List<CheckoutItem> Items { get; init; }
    public decimal TotalPrice { get; init; }
    public string ShippingAddress { get; init; }
    public string PaymentMethod { get; set; }
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

Це **event** буде consumed в **Ordering Service** для створення замовлення.

---

## Configuration

### appsettings.json

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "basket:"
  },
  
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest",
    "VirtualHost": "/"
  },
  
  "BasketSettings": {
    "MaxItems": 100,
    "MaxQuantityPerItem": 99,
    "BasketTTLDays": 30
  }
}
```

### Program.cs Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetValue<string>("Redis:ConnectionString")));

// MassTransit (RabbitMQ)
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetValue<string>("RabbitMQ:Host"));
        cfg.ConfigureEndpoints(context);
    });
});

// Repositories
builder.Services.AddScoped<IBasketRepository, RedisBasketRepository>();

// MediatR
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(CheckoutBasketCommand).Assembly));

var app = builder.Build();

app.MapBasketEndpoints();

app.Run();
```

---

## Merge Baskets (Anonymous → Logged In)

Коли анонімний користувач логіниться, потрібно об'єднати кошики:

```csharp
// EShop.Basket.Application/Basket/Commands/MergeBaskets/MergeBasketsHandler.cs

public class MergeBasketsHandler : IRequestHandler<MergeBasketsCommand>
{
    private readonly IBasketRepository _basketRepository;

    public async Task Handle(
        MergeBasketsCommand request,
        CancellationToken cancellationToken)
    {
        var anonymousBasket = await _basketRepository.GetBasketAsync(request.AnonymousId);
        var userBasket = await _basketRepository.GetBasketAsync(request.UserId) 
            ?? ShoppingBasket.Create(request.UserId);

        if (anonymousBasket is not null)
        {
            foreach (var item in anonymousBasket.Items)
            {
                userBasket.AddItem(
                    item.ProductId,
                    item.ProductName,
                    item.Price,
                    item.Quantity
                );
            }

            await _basketRepository.SaveBasketAsync(userBasket);
            await _basketRepository.DeleteBasketAsync(request.AnonymousId);
        }
    }
}
```

---

## Testing

### Unit Tests

```csharp
[Fact]
public void AddItem_WhenProductExists_ShouldIncreaseQuantity()
{
    // Arrange
    var basket = ShoppingBasket.Create("user123");
    basket.AddItem(Guid.NewGuid(), "Laptop", 1000, 1);
    
    // Act
    basket.AddItem(basket.Items.First().ProductId, "Laptop", 1000, 2);
    
    // Assert
    basket.Items.Should().HaveCount(1);
    basket.Items.First().Quantity.Should().Be(3);
}
```

---

## Наступні кроки

- ✅ [Ordering Service](ordering-service.md) - Consume BasketCheckedOutEvent
- ✅ [Redis Infrastructure](../../05-infrastructure/caching.md)
- ✅ [RabbitMQ Setup](../../05-infrastructure/message-broker.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
