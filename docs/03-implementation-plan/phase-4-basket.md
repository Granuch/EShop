# 🛒 Phase 4: Basket Service Implementation

**Duration**: 1.5 weeks  
**Team Size**: 1-2 developers  
**Prerequisites**: Phase 1, Phase 3 (Catalog) completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Shopping basket management (add, remove, update items)
- ✅ Redis as primary storage (no SQL database)
- ✅ Integration with Catalog Service (product validation)
- ✅ Checkout functionality
- ✅ Basket expiration (30 days)
- ✅ Anonymous baskets support
- ✅ Basket merge on login

---

## Architecture

**Storage**: Redis (in-memory, fast, ephemeral data)  
**Pattern**: Repository pattern with Redis client

```
EShop.Basket/
├── EShop.Basket.API/          # Controllers
├── EShop.Basket.Application/  # Commands, Queries
├── EShop.Basket.Domain/       # Entities, Value Objects
└── EShop.Basket.Infrastructure/ # Redis Repository
```

---

## Tasks Breakdown

### 4.1 Domain Layer

**Estimated Time**: 1 day

**ShoppingBasket Entity:**

```csharp
// EShop.Basket.Domain/Entities/ShoppingBasket.cs

public class ShoppingBasket
{
    public string UserId { get; set; } = string.Empty;
    public List<BasketItem> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public decimal TotalPrice => Items.Sum(i => i.Price * i.Quantity);
    public int TotalItems => Items.Sum(i => i.Quantity);

    public void AddItem(Guid productId, string productName, decimal price, int quantity = 1)
    {
        var existingItem = Items.FirstOrDefault(i => i.ProductId == productId);
        
        if (existingItem != null)
        {
            existingItem.Quantity += quantity;
        }
        else
        {
            Items.Add(new BasketItem
            {
                ProductId = productId,
                ProductName = productName,
                Price = price,
                Quantity = quantity
            });
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveItem(Guid productId)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            Items.Remove(item);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void UpdateQuantity(Guid productId, int quantity)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            if (quantity <= 0)
            {
                Items.Remove(item);
            }
            else
            {
                item.Quantity = quantity;
            }
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void Clear()
    {
        Items.Clear();
        UpdatedAt = DateTime.UtcNow;
    }
}

public class BasketItem
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string? ImageUrl { get; set; }
}
```

---

### 4.2 Infrastructure Layer (Redis Repository)

**Estimated Time**: 2 days

**RedisBasketRepository:**

```csharp
// EShop.Basket.Infrastructure/Repositories/RedisBasketRepository.cs

public class RedisBasketRepository : IBasketRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBasketRepository> _logger;
    private static readonly TimeSpan BasketExpiration = TimeSpan.FromDays(30);

    public RedisBasketRepository(IConnectionMultiplexer redis, ILogger<RedisBasketRepository> logger)
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
            _logger.LogInformation("Basket not found for user {UserId}", userId);
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
            BasketExpiration);

        if (!success)
        {
            _logger.LogError("Failed to save basket for user {UserId}", basket.UserId);
            throw new Exception("Failed to save basket");
        }

        _logger.LogInformation("Basket saved for user {UserId} with {ItemCount} items", 
            basket.UserId, basket.Items.Count);

        return basket;
    }

    public async Task<bool> DeleteBasketAsync(string userId)
    {
        var db = _redis.GetDatabase();
        var success = await db.KeyDeleteAsync(GetKey(userId));

        if (success)
            _logger.LogInformation("Basket deleted for user {UserId}", userId);

        return success;
    }

    private static string GetKey(string userId) => $"basket:{userId}";
}
```

---

### 4.3 Application Layer

**Estimated Time**: 2 days

**Add Item to Basket Command:**

```csharp
// EShop.Basket.Application/Commands/AddItemToBasket/AddItemToBasketCommand.cs

public record AddItemToBasketCommand : IRequest<Result<ShoppingBasket>>
{
    public string UserId { get; init; } = string.Empty;
    public Guid ProductId { get; init; }
    public int Quantity { get; init; } = 1;
}

public class AddItemToBasketCommandHandler : IRequestHandler<AddItemToBasketCommand, Result<ShoppingBasket>>
{
    private readonly IBasketRepository _basketRepository;
    private readonly IProductApiClient _productApiClient;
    private readonly ILogger<AddItemToBasketCommandHandler> _logger;

    public async Task<Result<ShoppingBasket>> Handle(
        AddItemToBasketCommand request,
        CancellationToken cancellationToken)
    {
        // Validate product exists and is in stock
        var product = await _productApiClient.GetProductAsync(request.ProductId);
        if (product == null)
        {
            return Result<ShoppingBasket>.Failure(
                new Error("PRODUCT_NOT_FOUND", "Product not found"));
        }

        if (product.StockQuantity < request.Quantity)
        {
            return Result<ShoppingBasket>.Failure(
                new Error("INSUFFICIENT_STOCK", $"Only {product.StockQuantity} items available"));
        }

        // Get or create basket
        var basket = await _basketRepository.GetBasketAsync(request.UserId)
            ?? new ShoppingBasket { UserId = request.UserId };

        // Add item
        basket.AddItem(
            productId: product.Id,
            productName: product.Name,
            price: product.Price,
            quantity: request.Quantity);

        // Save basket
        await _basketRepository.SaveBasketAsync(basket);

        _logger.LogInformation(
            "Item {ProductId} added to basket for user {UserId}",
            request.ProductId, request.UserId);

        return Result<ShoppingBasket>.Success(basket);
    }
}
```

**Checkout Basket Command:**

```csharp
// EShop.Basket.Application/Commands/CheckoutBasket/CheckoutBasketCommand.cs

public record CheckoutBasketCommand : IRequest<Result<Guid>>
{
    public string UserId { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
}

public class CheckoutBasketCommandHandler : IRequestHandler<CheckoutBasketCommand, Result<Guid>>
{
    private readonly IBasketRepository _basketRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<CheckoutBasketCommandHandler> _logger;

    public async Task<Result<Guid>> Handle(
        CheckoutBasketCommand request,
        CancellationToken cancellationToken)
    {
        var basket = await _basketRepository.GetBasketAsync(request.UserId);
        if (basket == null || !basket.Items.Any())
        {
            return Result<Guid>.Failure(
                new Error("EMPTY_BASKET", "Basket is empty"));
        }

        // Publish BasketCheckedOutEvent (will be consumed by Ordering Service)
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

        await _publishEndpoint.Publish(checkoutEvent, cancellationToken);

        // Clear basket
        await _basketRepository.DeleteBasketAsync(request.UserId);

        _logger.LogInformation("Basket checked out for user {UserId}", request.UserId);

        return Result<Guid>.Success(Guid.NewGuid()); // Temporary OrderId
    }
}
```

---

### 4.4 API Layer

**Estimated Time**: 1 day

**BasketController:**

```csharp
// EShop.Basket.API/Controllers/BasketController.cs

[ApiController]
[Route("api/v{version:apiVersion}/basket")]
[ApiVersion("1.0")]
[Authorize]
public class BasketController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<BasketController> _logger;

    [HttpGet]
    [ProducesResponseType(typeof(ShoppingBasket), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBasket(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();
        
        var query = new GetBasketQuery { UserId = userId };
        var result = await _mediator.Send(query, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : NotFound();
    }

    [HttpPost("items")]
    [ProducesResponseType(typeof(ShoppingBasket), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddItem(
        [FromBody] AddItemRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();

        var command = new AddItemToBasketCommand
        {
            UserId = userId,
            ProductId = request.ProductId,
            Quantity = request.Quantity
        };

        var result = await _mediator.Send(command, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("items/{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateQuantity(
        Guid productId,
        [FromBody] UpdateQuantityRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();

        var command = new UpdateBasketItemCommand
        {
            UserId = userId,
            ProductId = productId,
            Quantity = request.Quantity
        };

        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpDelete("items/{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveItem(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();

        var command = new RemoveBasketItemCommand
        {
            UserId = userId,
            ProductId = productId
        };

        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();

        var command = new CheckoutBasketCommand
        {
            UserId = userId,
            ShippingAddress = request.ShippingAddress,
            PaymentMethod = request.PaymentMethod
        };

        var result = await _mediator.Send(command, cancellationToken);

        return result.IsSuccess ? Ok(new { orderId = result.Value }) : BadRequest(result.Error);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearBasket(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value ?? throw new UnauthorizedAccessException();

        var command = new ClearBasketCommand { UserId = userId };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
```

---

### 4.5 Integration with Catalog Service

**Estimated Time**: 1 day

**Product API Client (HTTP Client):**

```csharp
// EShop.Basket.Infrastructure/Clients/ProductApiClient.cs

public interface IProductApiClient
{
    Task<ProductDto?> GetProductAsync(Guid productId);
}

public class ProductApiClient : IProductApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProductApiClient> _logger;

    public ProductApiClient(HttpClient httpClient, ILogger<ProductApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ProductDto?> GetProductAsync(Guid productId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v1/products/{productId}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Product {ProductId} not found", productId);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ProductDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching product {ProductId}", productId);
            return null;
        }
    }
}

// Register in Program.cs with Polly
builder.Services.AddHttpClient<IProductApiClient, ProductApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("CatalogApi:Url")!);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());
```

---

### 4.6 Testing

**Estimated Time**: 1 day

**Unit Tests:**

```csharp
public class AddItemToBasketCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithValidProduct_ShouldAddItemToBasket()
    {
        // Arrange
        var command = new AddItemToBasketCommand
        {
            UserId = "user123",
            ProductId = Guid.NewGuid(),
            Quantity = 2
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Quantity.Should().Be(2);
    }
}
```

---

## Success Criteria

- [x] Users can add/remove/update items in basket
- [x] Basket data stored in Redis
- [x] Product validation via Catalog API
- [x] Checkout publishes BasketCheckedOutEvent
- [x] Basket expires after 30 days
- [x] All tests passing (> 75% coverage)

---

## Next Phase

→ [Phase 5: Ordering Service Implementation](phase-5-ordering.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
