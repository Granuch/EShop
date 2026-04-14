# 📦 Catalog Service

Сервіс управління продуктами та категоріями з підтримкою CQRS, кешування та пошуку.

---

## Огляд

Catalog Service відповідає за:
- ✅ CRUD операції для продуктів
- ✅ Управління категоріями (з ієрархією)
- ✅ Пошук та фільтрація продуктів
- ✅ Пагінацію (cursor-based pagination)
- ✅ Завантаження зображень продуктів
- ✅ Кешування з Redis (Decorator pattern)
- ✅ Event publishing при змінах (ProductCreated, PriceChanged, OutOfStock)
- ✅ Управління атрибутами продуктів
- ✅ Підтримка знижок та промо-акцій

---

## Архітектура

### Технології

| Компонент | Технологія | Призначення |
|-----------|------------|-------------|
| **Framework** | ASP.NET Core 9.0 Minimal API | Web API |
| **Database** | PostgreSQL | Product storage |
| **Cache** | Redis | Read performance |
| **CQRS** | MediatR | Command/Query separation |
| **Validation** | FluentValidation | Input validation |
| **Mapping** | AutoMapper / Mapperly | DTO mapping |
| **Output Cache** | ASP.NET Core Output Caching | HTTP caching |
| **Search** | PostgreSQL Full-Text Search | Product search |

### Clean Architecture Layers

```
EShop.Catalog.API/
├── Endpoints/
│   ├── ProductEndpoints.cs         # GET, POST, PUT, DELETE /products
│   └── CategoryEndpoints.cs        # GET, POST, PUT, DELETE /categories
├── Filters/
│   └── ValidationFilter.cs
├── Program.cs
└── appsettings.json

EShop.Catalog.Domain/
├── Entities/
│   ├── Product.cs                  # Aggregate Root
│   ├── Category.cs
│   ├── ProductImage.cs            # Entity
│   └── ProductAttribute.cs         # Entity
├── ValueObjects/
│   ├── Money.cs                    # Price with currency
│   ├── ProductStatus.cs            # Enum: Draft, Active, Discontinued
│   └── Weight.cs
├── Interfaces/
│   ├── IProductRepository.cs
│   └── ICategoryRepository.cs
└── Events/
    ├── ProductCreatedEvent.cs
    ├── ProductUpdatedEvent.cs
    ├── ProductPriceChangedEvent.cs
    ├── ProductOutOfStockEvent.cs
    └── ProductBackInStockEvent.cs

EShop.Catalog.Application/
├── Products/
│   ├── Commands/
│   │   ├── CreateProduct/
│   │   │   ├── CreateProductCommand.cs
│   │   │   ├── CreateProductHandler.cs
│   │   │   └── CreateProductValidator.cs
│   │   ├── UpdateProduct/
│   │   ├── UpdateProductPrice/
│   │   ├── UpdateProductStock/
│   │   └── DeleteProduct/
│   └── Queries/
│       ├── GetProducts/
│       │   ├── GetProductsQuery.cs
│       │   ├── GetProductsHandler.cs
│       │   └── ProductsResponse.cs
│       ├── GetProductById/
│       └── SearchProducts/
├── Categories/
│   ├── Commands/
│   │   ├── CreateCategory/
│   │   └── UpdateCategory/
│   └── Queries/
│       ├── GetCategories/
│       └── GetCategoryWithProducts/
└── Common/
    ├── DTOs/
    │   ├── ProductDto.cs
    │   └── CategoryDto.cs
    ├── Mappings/
    │   └── ProductMappingProfile.cs
    └── Behaviors/
        ├── ValidationBehavior.cs
        └── LoggingBehavior.cs

EShop.Catalog.Infrastructure/
├── Data/
│   ├── CatalogDbContext.cs
│   ├── Configurations/
│   │   ├── ProductConfiguration.cs    # EF Core configuration
│   │   └── CategoryConfiguration.cs
│   └── Migrations/
├── Repositories/
│   ├── ProductRepository.cs
│   └── CategoryRepository.cs
├── Caching/
│   └── CachedProductRepository.cs     # Decorator pattern
├── Services/
│   ├── ImageStorageService.cs         # Upload to Azure Blob / S3
│   └── SearchService.cs               # PostgreSQL Full-Text Search
└── Extensions/
    └── ServiceCollectionExtensions.cs
```

---

## Domain Entities

### Product (Aggregate Root)

```csharp
// EShop.Catalog.Domain/Entities/Product.cs

public class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Sku { get; private set; } = null!;
    public Money Price { get; private set; } = null!;
    public Money? DiscountPrice { get; private set; }
    public int StockQuantity { get; private set; }
    public ProductStatus Status { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    
    private readonly List<ProductImage> _images = [];
    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();
    
    private readonly List<ProductAttribute> _attributes = [];
    public IReadOnlyCollection<ProductAttribute> Attributes => _attributes.AsReadOnly();

    // Audit fields
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public string? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    private Product() { } // EF Core

    // Factory method
    public static Product Create(
        string name,
        string? description,
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

        product.AddDomainEvent(new ProductCreatedEvent(product.Id, name, price.Amount));
        
        return product;
    }

    // Business logic methods
    public void UpdatePrice(Money newPrice, string updatedBy)
    {
        if (Price.Amount != newPrice.Amount)
        {
            var oldPrice = Price;
            Price = newPrice;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = updatedBy;
            
            AddDomainEvent(new ProductPriceChangedEvent(
                Id, oldPrice.Amount, newPrice.Amount));
        }
    }

    public void UpdateStock(int quantity, string updatedBy)
    {
        if (quantity < 0)
            throw new DomainException("Stock quantity cannot be negative");

        var oldQuantity = StockQuantity;
        StockQuantity = quantity;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;

        if (oldQuantity > 0 && quantity == 0)
            AddDomainEvent(new ProductOutOfStockEvent(Id));
        else if (oldQuantity == 0 && quantity > 0)
            AddDomainEvent(new ProductBackInStockEvent(Id));
    }

    public void Publish()
    {
        if (Status != ProductStatus.Draft)
            throw new DomainException("Only draft products can be published");

        Status = ProductStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete(string deletedBy)
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        UpdatedBy = deletedBy;
        Status = ProductStatus.Discontinued;
    }

    public void AddImage(string url, string? altText, int displayOrder)
    {
        var image = new ProductImage(Id, url, altText, displayOrder);
        _images.Add(image);
    }
}
```

### Money (Value Object)

```csharp
// EShop.Catalog.Domain/ValueObjects/Money.cs

public record Money
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";

    public Money(decimal amount, string currency = "USD")
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));

        Amount = amount;
        Currency = currency;
    }

    public static Money operator +(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException("Cannot add money with different currencies");

        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator *(Money money, decimal multiplier)
    {
        return new Money(money.Amount * multiplier, money.Currency);
    }

    public static Money Zero(string currency = "USD") => new(0, currency);

    public override string ToString() => $"{Amount:N2} {Currency}";
}
```

### Category

```csharp
// EShop.Catalog.Domain/Entities/Category.cs

public class Category : Entity<Guid>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Slug { get; private set; } = null!;
    public Guid? ParentCategoryId { get; private set; }
    public Category? ParentCategory { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    
    private readonly List<Category> _childCategories = [];
    public IReadOnlyCollection<Category> ChildCategories => _childCategories.AsReadOnly();
    
    private readonly List<Product> _products = [];
    public IReadOnlyCollection<Product> Products => _products.AsReadOnly();

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Category() { }

    public static Category Create(string name, string? description, Guid? parentId = null)
    {
        return new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Slug = GenerateSlug(name),
            ParentCategoryId = parentId,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string GenerateSlug(string name)
    {
        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and");
    }
}
```

---

## API Endpoints

### Product Endpoints

#### GET /api/v1/products

Отримати список продуктів з фільтрацією та пагінацією.

**Query Parameters:**
- `page` (int, default=1) - Page number
- `pageSize` (int, default=20, max=100) - Items per page
- `categoryId` (Guid, optional) - Filter by category
- `minPrice` (decimal, optional) - Min price filter
- `maxPrice` (decimal, optional) - Max price filter
- `status` (string, optional) - active | draft | discontinued
- `sortBy` (string, default=createdAt) - name | price | createdAt
- `sortOrder` (string, default=desc) - asc | desc

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "Laptop Dell XPS 15",
      "description": "High-performance laptop",
      "sku": "DELL-XPS15-001",
      "price": {
        "amount": 1499.99,
        "currency": "USD"
      },
      "discountPrice": null,
      "stockQuantity": 25,
      "status": "active",
      "categoryId": "...",
      "categoryName": "Laptops",
      "images": [
        {
          "url": "https://cdn.eshop.com/products/dell-xps15.jpg",
          "altText": "Dell XPS 15 Front View"
        }
      ],
      "createdAt": "2024-01-15T10:00:00Z"
    }
  ],
  "pagination": {
    "currentPage": 1,
    "pageSize": 20,
    "totalCount": 150,
    "totalPages": 8,
    "hasNextPage": true,
    "hasPreviousPage": false
  }
}
```

---

#### GET /api/v1/products/{id}

Отримати продукт за ID.

**Response:** `200 OK`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Laptop Dell XPS 15",
  "description": "High-performance laptop with Intel i7...",
  "sku": "DELL-XPS15-001",
  "price": {
    "amount": 1499.99,
    "currency": "USD"
  },
  "discountPrice": null,
  "stockQuantity": 25,
  "status": "active",
  "categoryId": "...",
  "categoryName": "Laptops",
  "images": [
    {
      "id": "...",
      "url": "https://cdn.eshop.com/products/dell-xps15-1.jpg",
      "altText": "Dell XPS 15 Front",
      "displayOrder": 1
    }
  ],
  "attributes": [
    {
      "name": "CPU",
      "value": "Intel Core i7-12700H"
    },
    {
      "name": "RAM",
      "value": "16GB DDR5"
    },
    {
      "name": "Storage",
      "value": "512GB NVMe SSD"
    }
  ],
  "createdAt": "2024-01-15T10:00:00Z",
  "updatedAt": "2024-01-15T14:30:00Z"
}
```

---

#### GET /api/v1/products/search?q={query}

Пошук продуктів за назвою/описом.

**Query Parameters:**
- `q` (string, required) - Search query
- `page` (int, default=1)
- `pageSize` (int, default=20)

**Response:** `200 OK` (Same structure as GET /products)

---

#### POST /api/v1/products

Створити новий продукт (Admin only).

**Request:**
```json
{
  "name": "Laptop Dell XPS 15",
  "description": "High-performance laptop",
  "sku": "DELL-XPS15-001",
  "price": {
    "amount": 1499.99,
    "currency": "USD"
  },
  "stockQuantity": 25,
  "categoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "images": [
    {
      "url": "https://cdn.eshop.com/products/dell-xps15.jpg",
      "altText": "Dell XPS 15"
    }
  ]
}
```

**Response:** `201 Created`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Laptop Dell XPS 15",
  "sku": "DELL-XPS15-001",
  "price": {
    "amount": 1499.99,
    "currency": "USD"
  },
  "status": "draft"
}
```

---

#### PUT /api/v1/products/{id}

Оновити продукт (Admin only).

**Request:**
```json
{
  "name": "Laptop Dell XPS 15 (Updated)",
  "description": "Updated description",
  "price": {
    "amount": 1399.99,
    "currency": "USD"
  },
  "stockQuantity": 30
}
```

**Response:** `204 No Content`

---

#### DELETE /api/v1/products/{id}

Видалити продукт (Soft delete, Admin only).

**Response:** `204 No Content`

---

### Category Endpoints

#### GET /api/v1/categories

Отримати всі категорії (з ієрархією).

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "...",
      "name": "Electronics",
      "slug": "electronics",
      "parentCategoryId": null,
      "childCategories": [
        {
          "id": "...",
          "name": "Laptops",
          "slug": "laptops",
          "parentCategoryId": "...",
          "productsCount": 45
        },
        {
          "id": "...",
          "name": "Smartphones",
          "slug": "smartphones",
          "productsCount": 78
        }
      ],
      "productsCount": 123
    }
  ]
}
```

---

## Implementation Examples

### Minimal API Endpoints

```csharp
// EShop.Catalog.API/Endpoints/ProductEndpoints.cs

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products")
            .WithOpenApi();

        group.MapGet("/", GetProducts)
            .WithName("GetProducts")
            .WithSummary("Get paginated list of products")
            .CacheOutput(policy => policy
                .Expire(TimeSpan.FromMinutes(5))
                .Tag("products"));

        group.MapGet("/{id:guid}", GetProductById)
            .WithName("GetProductById")
            .CacheOutput(policy => policy.Tag("products"));

        group.MapGet("/search", SearchProducts)
            .WithName("SearchProducts");

        group.MapPost("/", CreateProduct)
            .RequireAuthorization("Admin")
            .WithName("CreateProduct")
            .DisableAntiforgery();

        group.MapPut("/{id:guid}", UpdateProduct)
            .RequireAuthorization("Admin")
            .WithName("UpdateProduct");

        group.MapDelete("/{id:guid}", DeleteProduct)
            .RequireAuthorization("Admin")
            .WithName("DeleteProduct");

        return app;
    }

    private static async Task<IResult> GetProducts(
        [AsParameters] GetProductsQuery query,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(query, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetProductById(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var query = new GetProductByIdQuery(id);
        var result = await mediator.Send(query, ct);
        
        return result is null 
            ? Results.NotFound() 
            : Results.Ok(result);
    }

    private static async Task<IResult> CreateProduct(
        [FromBody] CreateProductCommand command,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        
        return result.Match(
            product => Results.CreatedAtRoute(
                "GetProductById", 
                new { id = product.Id }, 
                product),
            error => Results.BadRequest(error)
        );
    }
}
```

---

### CQRS Handler Example

```csharp
// EShop.Catalog.Application/Products/Queries/GetProducts/GetProductsHandler.cs

public class GetProductsHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IProductRepository _repository;
    private readonly IMapper _mapper;

    public GetProductsHandler(IProductRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<PagedResult<ProductDto>> Handle(
        GetProductsQuery request, 
        CancellationToken cancellationToken)
    {
        var products = await _repository.GetPagedAsync(
            page: request.Page,
            pageSize: request.PageSize,
            categoryId: request.CategoryId,
            minPrice: request.MinPrice,
            maxPrice: request.MaxPrice,
            status: request.Status,
            sortBy: request.SortBy,
            sortOrder: request.SortOrder,
            cancellationToken: cancellationToken
        );

        var productDtos = _mapper.Map<IEnumerable<ProductDto>>(products.Items);

        return new PagedResult<ProductDto>
        {
            Items = productDtos,
            Pagination = products.Pagination
        };
    }
}
```

---

### Cached Repository (Decorator Pattern)

```csharp
// EShop.Catalog.Infrastructure/Caching/CachedProductRepository.cs

public class CachedProductRepository : IProductRepository
{
    private readonly IProductRepository _decorated;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedProductRepository> _logger;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(10);

    public CachedProductRepository(
        IProductRepository decorated,
        IDistributedCache cache,
        ILogger<CachedProductRepository> logger)
    {
        _decorated = decorated;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var cacheKey = $"product:{id}";
        
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for product {ProductId}", id);
            return JsonSerializer.Deserialize<Product>(cached);
        }

        var product = await _decorated.GetByIdAsync(id, ct);
        
        if (product is not null)
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(product),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheExpiration
                },
                ct);
        }

        return product;
    }

    public async Task AddAsync(Product product, CancellationToken ct = default)
    {
        await _decorated.AddAsync(product, ct);
        await InvalidateCacheAsync(product.Id, ct);
    }

    public async Task UpdateAsync(Product product, CancellationToken ct = default)
    {
        await _decorated.UpdateAsync(product, ct);
        await InvalidateCacheAsync(product.Id, ct);
        await InvalidateListCacheAsync(ct);
    }

    private async Task InvalidateCacheAsync(Guid productId, CancellationToken ct)
    {
        await _cache.RemoveAsync($"product:{productId}", ct);
    }

    private async Task InvalidateListCacheAsync(CancellationToken ct)
    {
        // Invalidate cached lists (can use tag-based invalidation)
        _logger.LogInformation("Invalidating product list cache");
    }
}
```

---

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=catalog;Username=eshop;Password=eshop123"
  },
  
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "catalog:"
  },
  
  "OutputCache": {
    "DefaultExpiration": 300,
    "ProductsExpiration": 600
  },
  
  "Pagination": {
    "DefaultPageSize": 20,
    "MaxPageSize": 100
  },
  
  "ImageStorage": {
    "Provider": "AzureBlob",
    "ConnectionString": "...",
    "ContainerName": "product-images"
  }
}
```

### Program.cs Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .UseSnakeCaseNamingConvention());

// Redis Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetValue<string>("Redis:ConnectionString");
    options.InstanceName = builder.Configuration.GetValue<string>("Redis:InstanceName");
});

// Output Caching
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder.Tag("products"));
    options.AddPolicy("products", builder => 
        builder.Expire(TimeSpan.FromMinutes(10)));
});

// MediatR (CQRS)
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(GetProductsQuery).Assembly));

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<CreateProductValidator>();

// Repositories (Decorator pattern for caching)
builder.Services.AddScoped<ProductRepository>();
builder.Services.AddScoped<IProductRepository, CachedProductRepository>();

var app = builder.Build();

app.UseOutputCache();

// Map endpoints
app.MapProductEndpoints();
app.MapCategoryEndpoints();

app.Run();
```

---

## Events

### ProductPriceChangedEvent

```csharp
// EShop.Catalog.Domain/Events/ProductPriceChangedEvent.cs

public record ProductPriceChangedEvent(
    Guid ProductId,
    decimal OldPrice,
    decimal NewPrice,
    DateTime OccurredAt) : IDomainEvent;
```

Consumers:
- **Basket Service** - Update basket items if price changed
- **Notification Service** - Send price drop alerts to users
- **Analytics Service** - Track price history

---

## Testing

### Unit Tests

```csharp
[Fact]
public void UpdatePrice_WhenPriceChanged_ShouldRaiseDomainEvent()
{
    // Arrange
    var product = Product.Create("Laptop", null, "SKU001", 
        new Money(1000), 10, Guid.NewGuid(), "admin");
    var newPrice = new Money(900);
    
    // Act
    product.UpdatePrice(newPrice, "admin");
    
    // Assert
    product.DomainEvents.Should().ContainSingle()
        .Which.Should().BeOfType<ProductPriceChangedEvent>();
}
```

### Integration Tests

```csharp
[Fact]
public async Task GET_Products_ShouldReturnPagedResults()
{
    // Arrange
    await SeedProducts(25);
    
    // Act
    var response = await _client.GetAsync("/api/v1/products?page=1&pageSize=10");
    var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
    
    // Assert
    result.Items.Should().HaveCount(10);
    result.Pagination.TotalCount.Should().Be(25);
}
```

---

## Performance Optimization

### ✅ Implemented

1. **Redis Caching** - 10min TTL for product details
2. **Output Caching** - 5min for product lists
3. **Pagination** - Cursor-based for large datasets
4. **Eager Loading** - Include Category, Images
5. **Index Optimization** - On SKU, CategoryId, Status

### Potential Improvements

- Elasticsearch for advanced search
- CDN for product images
- GraphQL for flexible queries
- Read replicas for PostgreSQL

---

## Наступні кроки

- ✅ [Basket Service](basket-service.md) - Shopping cart
- ✅ [Ordering Service](ordering-service.md) - Order management
- ✅ [Infrastructure - Caching](../../06-infrastructure/caching.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
