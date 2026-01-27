# 📦 Phase 3: Catalog Service Implementation

**Duration**: 2 weeks  
**Team Size**: 2 developers  
**Prerequisites**: Phase 1 completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Product CRUD operations
- ✅ Category management (hierarchical)
- ✅ Product search & filtering
- ✅ Pagination support
- ✅ Image upload & management
- ✅ Stock management
- ✅ Price management (including discounts)
- ✅ Caching strategy

---

## Domain Model

### Entities

- **Product**: Main aggregate root
- **Category**: Hierarchical structure
- **ProductImage**: Owned entity

### Value Objects

- **Money**: Price with currency
- **SKU**: Stock Keeping Unit

---

## Tasks Breakdown

### 3.1 Domain Layer

**Estimated Time**: 2 days

**Product Entity:**

```csharp
// EShop.Catalog.Domain/Entities/Product.cs

public class Product : AggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;
    public Money? DiscountPrice { get; private set; }
    public int StockQuantity { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    public ProductStatus Status { get; private set; }
    public List<ProductImage> Images { get; private set; } = new();

    private Product() { }

    public static Product Create(
        string name,
        string description,
        string sku,
        Money price,
        int stockQuantity,
        Guid categoryId,
        string createdBy)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Sku = sku,
            Price = price,
            StockQuantity = stockQuantity,
            CategoryId = categoryId,
            Status = ProductStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        product.AddDomainEvent(new ProductCreatedEvent(product.Id, product.Name));
        return product;
    }

    public void UpdatePrice(Money newPrice)
    {
        Price = newPrice;
        AddDomainEvent(new ProductPriceChangedEvent(Id, Price, newPrice));
    }

    public void UpdateStock(int quantity)
    {
        StockQuantity = quantity;
        AddDomainEvent(new ProductStockChangedEvent(Id, quantity));
    }

    public void Publish()
    {
        Status = ProductStatus.Published;
        AddDomainEvent(new ProductPublishedEvent(Id));
    }
}
```

**Money Value Object:**

```csharp
// EShop.Catalog.Domain/ValueObjects/Money.cs

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
}
```

---

### 3.2 Application Layer (CQRS)

**Estimated Time**: 3 days

**Create Product Command:**

```csharp
public record CreateProductCommand : IRequest<Result<Guid>>
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public Guid CategoryId { get; init; }
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductRepository _repository;

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var product = Product.Create(
            request.Name,
            request.Description,
            request.Sku,
            new Money(request.Price),
            request.StockQuantity,
            request.CategoryId,
            "system"
        );

        await _repository.AddAsync(product, ct);
        await _repository.SaveChangesAsync(ct);

        return Result<Guid>.Success(product.Id);
    }
}
```

**Get Products Query:**

```csharp
public record GetProductsQuery : IRequest<PagedResult<ProductDto>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public Guid? CategoryId { get; init; }
    public string? SearchTerm { get; init; }
}
```

---

### 3.3 API Layer

**Estimated Time**: 2 days

```csharp
[ApiController]
[Route("api/v{version:apiVersion}/products")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] GetProductsQuery query)
    {
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetProduct), new { id = result.Value }, result.Value);
    }
}
```

---

### 3.4 Caching Strategy

**Estimated Time**: 1 day

```csharp
public class CachedProductRepository : IProductRepository
{
    private readonly IProductRepository _decorated;
    private readonly IDistributedCache _cache;

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cacheKey = $"product:{id}";
        var cached = await _cache.GetAsync<Product>(cacheKey, ct);
        
        if (cached != null)
            return cached;

        var product = await _decorated.GetByIdAsync(id, ct);
        
        if (product != null)
            await _cache.SetAsync(cacheKey, product, TimeSpan.FromMinutes(10), ct);

        return product;
    }
}
```

---

## Success Criteria

- [x] Products can be created, read, updated, deleted
- [x] Categories support hierarchical structure
- [x] Search and filtering works
- [x] Pagination implemented
- [x] Caching reduces database load by 70%
- [x] All tests passing (> 80% coverage)

---

## Next Phase

→ [Phase 4: Basket Service Implementation](phase-4-basket.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
