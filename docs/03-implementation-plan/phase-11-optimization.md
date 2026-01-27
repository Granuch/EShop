# ⚡ Phase 11: Performance Optimization

**Duration**: 1.5 weeks  
**Team Size**: 2 developers  
**Prerequisites**: Phase 10 (DevOps) completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Database query optimization
- ✅ Caching strategy refinement
- ✅ API response time optimization (< 200ms p95)
- ✅ Frontend performance (Core Web Vitals)
- ✅ Load testing and bottleneck identification
- ✅ CDN integration
- ✅ Database connection pooling tuning

---

## Tasks Breakdown

### 11.1 Database Optimization

**Estimated Time**: 2 days

**Index Analysis:**

```sql
-- Find missing indexes
SELECT 
    schemaname,
    tablename,
    attname,
    n_distinct,
    correlation
FROM pg_stats
WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
  AND n_distinct > 100
ORDER BY n_distinct DESC;

-- Find unused indexes
SELECT
    schemaname,
    tablename,
    indexname,
    idx_scan,
    idx_tup_read,
    idx_tup_fetch
FROM pg_stat_user_indexes
WHERE idx_scan = 0
ORDER BY pg_relation_size(indexrelid) DESC;
```

**Add Missing Indexes:**

```sql
-- Products table
CREATE INDEX idx_products_category_status 
ON products(category_id, status) 
WHERE is_deleted = false;

CREATE INDEX idx_products_price 
ON products(price_amount) 
WHERE status = 'Published';

-- Orders table
CREATE INDEX idx_orders_user_created 
ON orders(user_id, created_at DESC);

CREATE INDEX idx_orders_status 
ON orders(status) 
WHERE status IN ('Pending', 'Paid');

-- Full-text search
CREATE INDEX idx_products_search 
ON products USING GIN(to_tsvector('english', name || ' ' || description));
```

**Query Optimization:**

```csharp
// ❌ Bad - N+1 query problem
var products = await _context.Products.ToListAsync();
foreach (var product in products)
{
    var category = await _context.Categories.FindAsync(product.CategoryId);
}

// ✅ Good - Eager loading
var products = await _context.Products
    .Include(p => p.Category)
    .Include(p => p.Images)
    .ToListAsync();

// ✅ Better - Projection (only needed fields)
var products = await _context.Products
    .Select(p => new ProductListDto
    {
        Id = p.Id,
        Name = p.Name,
        Price = p.Price.Amount,
        CategoryName = p.Category.Name,
        ImageUrl = p.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault()!.Url
    })
    .ToListAsync();
```

**Connection Pooling:**

```csharp
// appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres;Database=catalog;Username=eshop;Password=***;Pooling=true;MinPoolSize=10;MaxPoolSize=100;ConnectionIdleLifetime=300"
  }
}
```

---

### 11.2 Caching Strategy Refinement

**Estimated Time**: 2 days

**Multi-Level Caching:**

```csharp
// Level 1: In-Memory Cache (fastest, per-instance)
// Level 2: Distributed Cache (Redis, shared across instances)

public class MultiLevelCacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;

    public async Task<T?> GetAsync<T>(string key)
    {
        // Try L1 cache (memory)
        if (_memoryCache.TryGetValue(key, out T? value))
            return value;

        // Try L2 cache (Redis)
        value = await _distributedCache.GetAsync<T>(key);
        
        if (value != null)
        {
            // Store in L1 cache
            _memoryCache.Set(key, value, TimeSpan.FromMinutes(5));
        }

        return value;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration)
    {
        // Set in both caches
        _memoryCache.Set(key, value, expiration);
        await _distributedCache.SetAsync(key, value, expiration);
    }
}
```

**Cache Stampede Prevention:**

```csharp
public class CacheService
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiration)
    {
        var cached = await _cache.GetAsync<T>(key);
        if (cached != null)
            return cached;

        await _semaphore.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            cached = await _cache.GetAsync<T>(key);
            if (cached != null)
                return cached;

            var value = await factory();
            await _cache.SetAsync(key, value, expiration);
            return value;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

**Cache Invalidation Strategy:**

```csharp
// Tag-based invalidation
public class TaggedCacheService
{
    public async Task SetWithTagsAsync<T>(
        string key,
        T value,
        string[] tags,
        TimeSpan expiration)
    {
        await _cache.SetAsync(key, value, expiration);

        // Store tags
        foreach (var tag in tags)
        {
            var tagKey = $"tag:{tag}";
            var keys = await _cache.GetAsync<HashSet<string>>(tagKey) ?? new HashSet<string>();
            keys.Add(key);
            await _cache.SetAsync(tagKey, keys, expiration);
        }
    }

    public async Task InvalidateByTagAsync(string tag)
    {
        var tagKey = $"tag:{tag}";
        var keys = await _cache.GetAsync<HashSet<string>>(tagKey);

        if (keys != null)
        {
            foreach (var key in keys)
            {
                await _cache.RemoveAsync(key);
            }
            await _cache.RemoveAsync(tagKey);
        }
    }
}

// Usage
await _cache.SetWithTagsAsync("product:123", product, ["products", "category:electronics"], TimeSpan.FromMinutes(10));

// Invalidate all products
await _cache.InvalidateByTagAsync("products");
```

---

### 11.3 API Response Time Optimization

**Estimated Time**: 2 days

**Response Compression:**

```csharp
// Program.cs
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
    options.Providers.Add<BrotliCompressionProvider>();
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

app.UseResponseCompression();
```

**Output Caching (ASP.NET Core 9):**

```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder.Expire(TimeSpan.FromMinutes(5)));

    options.AddPolicy("products", builder => builder
        .Expire(TimeSpan.FromMinutes(10))
        .Tag("products")
        .SetVaryByQuery("page", "pageSize", "category"));
});

app.MapGet("/api/v1/products", GetProducts)
    .CacheOutput("products");
```

**Async Streaming:**

```csharp
// For large datasets, stream results
[HttpGet("export")]
public async IAsyncEnumerable<ProductDto> ExportProducts([EnumeratorCancellation] CancellationToken cancellationToken)
{
    await foreach (var product in _repository.StreamProductsAsync(cancellationToken))
    {
        yield return MapToDto(product);
    }
}
```

**GraphQL Data Loader (for N+1 prevention):**

```csharp
public class ProductDataLoader : BatchDataLoader<Guid, Product>
{
    private readonly IProductRepository _repository;

    public ProductDataLoader(IProductRepository repository, IBatchScheduler batchScheduler)
        : base(batchScheduler)
    {
        _repository = repository;
    }

    protected override async Task<IReadOnlyDictionary<Guid, Product>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken)
    {
        var products = await _repository.GetByIdsAsync(keys, cancellationToken);
        return products.ToDictionary(p => p.Id);
    }
}
```

---

### 11.4 Frontend Performance

**Estimated Time**: 2 days

**Code Splitting:**

```tsx
// Lazy load routes
const ProductsPage = lazy(() => import('./pages/Products'));
const CheckoutPage = lazy(() => import('./pages/Checkout'));

function App() {
  return (
    <Suspense fallback={<LoadingSpinner />}>
      <Routes>
        <Route path="/products" element={<ProductsPage />} />
        <Route path="/checkout" element={<CheckoutPage />} />
      </Routes>
    </Suspense>
  );
}
```

**Image Optimization:**

```tsx
// Use modern formats (WebP, AVIF)
<picture>
  <source srcset={`${product.imageUrl}.avif`} type="image/avif" />
  <source srcset={`${product.imageUrl}.webp`} type="image/webp" />
  <img src={product.imageUrl} alt={product.name} loading="lazy" />
</picture>

// Or use Next.js Image component
import Image from 'next/image';

<Image
  src={product.imageUrl}
  alt={product.name}
  width={300}
  height={300}
  loading="lazy"
  placeholder="blur"
/>
```

**Bundle Size Optimization:**

```bash
# Analyze bundle
npm run build
npm install -D webpack-bundle-analyzer

# Tree shaking
# Remove unused lodash functions
import debounce from 'lodash/debounce'; # ✅ Good
import { debounce } from 'lodash';      # ❌ Bad (imports entire library)
```

**React Query Optimization:**

```tsx
// Prefetch data
const queryClient = useQueryClient();

const prefetchProduct = (productId: string) => {
  queryClient.prefetchQuery({
    queryKey: ['product', productId],
    queryFn: () => productsApi.getProductById(productId)
  });
};

// Use on hover
<Link
  to={`/products/${product.id}`}
  onMouseEnter={() => prefetchProduct(product.id)}
>
  {product.name}
</Link>
```

---

### 11.5 CDN Integration

**Estimated Time**: 1 day

**Azure CDN:**

```hcl
# infrastructure/terraform/cdn.tf

resource "azurerm_cdn_profile" "eshop" {
  name                = "eshop-cdn"
  location            = "global"
  resource_group_name = azurerm_resource_group.eshop.name
  sku                 = "Standard_Microsoft"
}

resource "azurerm_cdn_endpoint" "eshop" {
  name                = "eshop"
  profile_name        = azurerm_cdn_profile.eshop.name
  location            = azurerm_cdn_profile.eshop.location
  resource_group_name = azurerm_resource_group.eshop.name

  origin {
    name      = "eshop-origin"
    host_name = azurerm_storage_account.eshop_static.primary_blob_host
  }

  optimization_type = "GeneralWebDelivery"

  global_delivery_rule {
    cache_expiration_action {
      behavior = "Override"
      duration = "7.00:00:00" # 7 days
    }
  }
}
```

---

### 11.6 Load Testing & Benchmarking

**Estimated Time**: 2 days

**K6 Stress Test:**

```javascript
// tests/performance/stress-test.js

import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '2m', target: 100 },   // Ramp up
    { duration: '5m', target: 100 },   // Stay at 100
    { duration: '2m', target: 200 },   // Ramp up to 200
    { duration: '5m', target: 200 },   // Stay at 200
    { duration: '2m', target: 500 },   // Spike to 500
    { duration: '5m', target: 500 },   // Stay at 500
    { duration: '5m', target: 0 },     // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<500', 'p(99)<1000'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const responses = http.batch([
    ['GET', 'http://api.eshop.com/api/v1/products'],
    ['GET', 'http://api.eshop.com/api/v1/categories'],
    ['GET', 'http://api.eshop.com/api/v1/products/featured'],
  ]);

  check(responses[0], {
    'products status is 200': (r) => r.status === 200,
  });

  sleep(1);
}
```

**BenchmarkDotNet:**

```csharp
[MemoryDiagnoser]
public class ProductRepositoryBenchmarks
{
    [Benchmark]
    public async Task GetProducts_Cached()
    {
        var products = await _cachedRepository.GetProductsAsync(1, 20);
    }

    [Benchmark]
    public async Task GetProducts_NoCaching()
    {
        var products = await _repository.GetProductsAsync(1, 20);
    }
}
```

---

## Performance Targets

| Metric | Target | Current | Status |
|--------|--------|---------|--------|
| API Response Time (p95) | < 200ms | TBD | 🔄 |
| API Response Time (p99) | < 500ms | TBD | 🔄 |
| Database Query Time (p95) | < 50ms | TBD | 🔄 |
| Frontend FCP | < 1.5s | TBD | 🔄 |
| Frontend LCP | < 2.5s | TBD | 🔄 |
| Frontend TTI | < 3.5s | TBD | 🔄 |
| Cache Hit Rate | > 85% | TBD | 🔄 |

---

## Success Criteria

- [x] API p95 response time < 200ms
- [x] Database queries optimized with indexes
- [x] Caching hit rate > 85%
- [x] Frontend Core Web Vitals in "Good" range
- [x] CDN configured for static assets
- [x] Load tests passing at 500 RPS

---

## Next Phase

→ [Phase 12: Production Launch](phase-12-launch.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
