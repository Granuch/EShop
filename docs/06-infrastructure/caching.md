# 🚀 Caching (Redis)

Redis налаштування, стратегії кешування та best practices для високої продуктивності.

---

## Огляд

Redis використовується для:
- ✅ **Distributed Cache** - Кешування даних між інстансами сервісів
- ✅ **Session Storage** - Shopping baskets (Basket Service)
- ✅ **Rate Limiting** - Token bucket для API Gateway
- ✅ **Pub/Sub** - Real-time notifications (опційно)
- ✅ **Token Storage** - Refresh tokens (Identity Service)

---

## Redis Setup

### Docker Compose

```yaml
# deploy/docker/docker-compose.yml

services:
  redis:
    image: redis:7-alpine
    container_name: eshop-redis
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    command: >
      redis-server
      --appendonly yes
      --requirepass eshop123
      --maxmemory 512mb
      --maxmemory-policy allkeys-lru
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - eshop-network

volumes:
  redis_data:

networks:
  eshop-network:
    driver: bridge
```

---

### ASP.NET Core Configuration

#### appsettings.json

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379,password=eshop123",
    "InstanceName": "eshop:",
    "DefaultExpirationMinutes": 10,
    "EnableLogging": true
  }
}
```

#### Program.cs

```csharp
// Add Redis Distributed Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetValue<string>("Redis:ConnectionString");
    options.InstanceName = builder.Configuration.GetValue<string>("Redis:InstanceName");
});

// OR use IConnectionMultiplexer for advanced scenarios
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse(
        builder.Configuration.GetValue<string>("Redis:ConnectionString")!,
        true);

    configuration.AbortOnConnectFail = false;
    configuration.ConnectTimeout = 5000;
    configuration.SyncTimeout = 5000;

    return ConnectionMultiplexer.Connect(configuration);
});
```

---

## Caching Strategies

### 1. Cache-Aside (Lazy Loading) ⭐ Most Common

```csharp
public async Task<Product?> GetProductByIdAsync(Guid id, CancellationToken ct = default)
{
    var cacheKey = $"product:{id}";
    
    // Try get from cache
    var cached = await _cache.GetStringAsync(cacheKey, ct);
    if (!string.IsNullOrEmpty(cached))
    {
        _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
        return JsonSerializer.Deserialize<Product>(cached);
    }

    // Cache miss - get from database
    _logger.LogDebug("Cache miss for {CacheKey}", cacheKey);
    var product = await _repository.GetByIdAsync(id, ct);

    if (product is not null)
    {
        // Store in cache
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(product),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            },
            ct);
    }

    return product;
}
```

---

### 2. Write-Through

```csharp
public async Task UpdateProductAsync(Product product, CancellationToken ct = default)
{
    // Update database
    await _repository.UpdateAsync(product, ct);
    
    // Update cache immediately
    var cacheKey = $"product:{product.Id}";
    await _cache.SetStringAsync(
        cacheKey,
        JsonSerializer.Serialize(product),
        new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        },
        ct);
}
```

---

### 3. Write-Behind (Write-Back)

```csharp
// Write to cache first, then queue database update
public async Task UpdateProductPriceAsync(Guid productId, decimal newPrice)
{
    var cacheKey = $"product:{productId}";
    
    // Update cache immediately
    var product = await GetProductByIdAsync(productId);
    product.UpdatePrice(newPrice);
    await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(product));
    
    // Queue database update (via background job or message queue)
    await _backgroundJobClient.Enqueue<UpdateProductPriceJob>(
        job => job.Execute(productId, newPrice));
}
```

---

### 4. Cache Invalidation

```csharp
public async Task DeleteProductAsync(Guid id, CancellationToken ct = default)
{
    // Delete from database
    await _repository.DeleteAsync(id, ct);
    
    // Invalidate cache
    await _cache.RemoveAsync($"product:{id}", ct);
    
    // Invalidate related caches
    await _cache.RemoveAsync("products:list", ct);
    await _cache.RemoveAsync($"category:{product.CategoryId}:products", ct);
}
```

---

## Redis Data Structures

### Strings (Simple Key-Value)

```csharp
// Set
await db.StringSetAsync("user:123:name", "John Doe");

// Get
var name = await db.StringGetAsync("user:123:name");

// Set with expiration
await db.StringSetAsync("session:abc", "data", TimeSpan.FromMinutes(30));

// Increment (atomic)
await db.StringIncrementAsync("page:views:123");
```

---

### Hashes (Objects)

```csharp
// Store object as hash
var hashEntries = new HashEntry[]
{
    new("id", "123"),
    new("name", "John Doe"),
    new("email", "john@example.com")
};
await db.HashSetAsync("user:123", hashEntries);

// Get field
var email = await db.HashGetAsync("user:123", "email");

// Get all fields
var user = await db.HashGetAllAsync("user:123");
```

---

### Lists (Queues, Stacks)

```csharp
// Push to list (queue)
await db.ListRightPushAsync("queue:jobs", "job1");

// Pop from list
var job = await db.ListLeftPopAsync("queue:jobs");

// Get range
var items = await db.ListRangeAsync("recent:products", 0, 9); // Top 10
```

---

### Sets (Unique Collections)

```csharp
// Add to set
await db.SetAddAsync("tags:product:123", new RedisValue[] { "laptop", "electronics" });

// Check membership
var isMember = await db.SetContainsAsync("tags:product:123", "laptop");

// Get all members
var tags = await db.SetMembersAsync("tags:product:123");

// Set operations
var intersection = await db.SetCombineAsync(SetOperation.Intersect, 
    "tags:product:123", "tags:product:456");
```

---

### Sorted Sets (Leaderboards, Rankings)

```csharp
// Add with score
await db.SortedSetAddAsync("leaderboard", "player1", 100);
await db.SortedSetAddAsync("leaderboard", "player2", 150);

// Get rank
var rank = await db.SortedSetRankAsync("leaderboard", "player1");

// Get top 10
var topPlayers = await db.SortedSetRangeByRankAsync("leaderboard", 0, 9, Order.Descending);
```

---

## Distributed Caching Patterns

### Repository Pattern with Decorator

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
        var cacheKey = CacheKeys.Product(id);
        
        // Try cache
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit: {CacheKey}", cacheKey);
            return JsonSerializer.Deserialize<Product>(cached);
        }

        // Get from database
        _logger.LogDebug("Cache miss: {CacheKey}", cacheKey);
        var product = await _decorated.GetByIdAsync(id, ct);
        
        if (product is not null)
        {
            await SetCacheAsync(cacheKey, product, ct);
        }

        return product;
    }

    public async Task<IEnumerable<Product>> GetByCategoryAsync(
        Guid categoryId, 
        CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.ProductsByCategory(categoryId);
        
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            return JsonSerializer.Deserialize<IEnumerable<Product>>(cached)!;
        }

        var products = await _decorated.GetByCategoryAsync(categoryId, ct);
        
        await SetCacheAsync(cacheKey, products, ct);
        
        return products;
    }

    public async Task AddAsync(Product product, CancellationToken ct = default)
    {
        await _decorated.AddAsync(product, ct);
        await InvalidateCacheAsync(product, ct);
    }

    public async Task UpdateAsync(Product product, CancellationToken ct = default)
    {
        await _decorated.UpdateAsync(product, ct);
        await InvalidateCacheAsync(product, ct);
    }

    private async Task SetCacheAsync<T>(string key, T value, CancellationToken ct)
    {
        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(value),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheExpiration
            },
            ct);
    }

    private async Task InvalidateCacheAsync(Product product, CancellationToken ct)
    {
        // Invalidate product cache
        await _cache.RemoveAsync(CacheKeys.Product(product.Id), ct);
        
        // Invalidate category cache
        await _cache.RemoveAsync(CacheKeys.ProductsByCategory(product.CategoryId), ct);
        
        // Invalidate list caches
        await _cache.RemoveAsync(CacheKeys.AllProducts(), ct);
    }
}

// Cache key helpers
public static class CacheKeys
{
    public static string Product(Guid id) => $"product:{id}";
    public static string ProductsByCategory(Guid categoryId) => $"category:{categoryId}:products";
    public static string AllProducts() => "products:all";
}
```

---

### Registering Decorator

```csharp
// Program.cs

// Register concrete repository
builder.Services.AddScoped<ProductRepository>();

// Register cached decorator
builder.Services.AddScoped<IProductRepository, CachedProductRepository>();
```

---

## ASP.NET Core Output Caching (HTTP-level)

**New in .NET 9** - Response caching:

```csharp
// Program.cs
builder.Services.AddOutputCache(options =>
{
    // Default policy
    options.AddBasePolicy(builder => 
        builder.Expire(TimeSpan.FromMinutes(5)));

    // Named policy
    options.AddPolicy("products", builder => 
        builder
            .Expire(TimeSpan.FromMinutes(10))
            .Tag("products")
            .SetVaryByQuery("page", "pageSize", "category"));
});

var app = builder.Build();

app.UseOutputCache();

// Use in endpoints
app.MapGet("/api/v1/products", GetProducts)
    .CacheOutput("products");
```

**Invalidate by tag:**

```csharp
public class ProductService
{
    private readonly IOutputCacheStore _cacheStore;

    public async Task UpdateProductAsync(Product product)
    {
        await _repository.UpdateAsync(product);
        
        // Invalidate all cached responses tagged with "products"
        await _cacheStore.EvictByTagAsync("products", default);
    }
}
```

---

## Basket Service (Full Redis Storage)

```csharp
// EShop.Basket.Infrastructure/Repositories/RedisBasketRepository.cs

public class RedisBasketRepository : IBasketRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBasketRepository> _logger;
    private static readonly TimeSpan BasketTTL = TimeSpan.FromDays(30);

    public async Task<ShoppingBasket?> GetBasketAsync(string userId)
    {
        var db = _redis.GetDatabase();
        var data = await db.StringGetAsync(GetKey(userId));

        if (data.IsNullOrEmpty)
            return null;

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
            throw new Exception("Failed to save basket");

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

## Rate Limiting with Redis

```csharp
// Token bucket algorithm

public class RedisRateLimiter
{
    private readonly IDatabase _db;
    private readonly int _maxTokens;
    private readonly TimeSpan _refillInterval;

    public async Task<bool> AllowRequestAsync(string userId)
    {
        var key = $"rate_limit:{userId}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        // Lua script for atomic operation
        var script = @"
            local tokens_key = KEYS[1]
            local timestamp_key = KEYS[2]
            local max_tokens = tonumber(ARGV[1])
            local refill_rate = tonumber(ARGV[2])
            local now = tonumber(ARGV[3])
            
            local tokens = tonumber(redis.call('get', tokens_key) or max_tokens)
            local last_refill = tonumber(redis.call('get', timestamp_key) or now)
            
            local elapsed = now - last_refill
            local refill_amount = math.floor(elapsed * refill_rate)
            tokens = math.min(max_tokens, tokens + refill_amount)
            
            if tokens >= 1 then
                tokens = tokens - 1
                redis.call('set', tokens_key, tokens)
                redis.call('set', timestamp_key, now)
                return 1
            else
                return 0
            end
        ";

        var result = await _db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { $"{key}:tokens", $"{key}:timestamp" },
            new RedisValue[] { _maxTokens, 1.0 / _refillInterval.TotalSeconds, now }
        );

        return (int)result == 1;
    }
}
```

---

## Cache Monitoring

### Redis CLI

```bash
# Connect to Redis
docker exec -it eshop-redis redis-cli -a eshop123

# Monitor commands in real-time
MONITOR

# Get all keys
KEYS *

# Get info
INFO

# Get memory stats
INFO memory

# Get key TTL
TTL product:123

# Get keyspace stats
INFO keyspace
```

---

### RedisInsight (GUI)

```yaml
# Add RedisInsight to docker-compose

services:
  redis-insight:
    image: redislabs/redisinsight:latest
    container_name: redis-insight
    ports:
      - "8001:8001"
    volumes:
      - redisinsight_data:/db
    networks:
      - eshop-network

volumes:
  redisinsight_data:
```

Access: http://localhost:8001

---

## Performance Tuning

### 1. Serialization Options

**System.Text.Json (Default, Fastest):**
```csharp
var json = JsonSerializer.Serialize(product);
var product = JsonSerializer.Deserialize<Product>(json);
```

**MessagePack (Smaller Size):**
```csharp
var bytes = MessagePackSerializer.Serialize(product);
var product = MessagePackSerializer.Deserialize<Product>(bytes);
```

---

### 2. Compression

```csharp
public static class CacheExtensions
{
    public static async Task SetCompressedAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        DistributedCacheEntryOptions options)
    {
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress))
        {
            await gzip.WriteAsync(bytes);
        }
        
        await cache.SetAsync(key, output.ToArray(), options);
    }

    public static async Task<T?> GetCompressedAsync<T>(
        this IDistributedCache cache,
        string key)
    {
        var compressed = await cache.GetAsync(key);
        if (compressed is null)
            return default;
        
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(json);
    }
}
```

---

### 3. Pipeline Commands

```csharp
// Batch multiple operations
var batch = db.CreateBatch();

var task1 = batch.StringSetAsync("key1", "value1");
var task2 = batch.StringSetAsync("key2", "value2");
var task3 = batch.StringSetAsync("key3", "value3");

batch.Execute();

await Task.WhenAll(task1, task2, task3);
```

---

## Cache Eviction Policies

Configure in Redis:

```conf
maxmemory 512mb
maxmemory-policy allkeys-lru
```

**Policies:**
- `noeviction` - Return errors when memory limit is reached
- `allkeys-lru` - ⭐ Remove least recently used keys (recommended)
- `volatile-lru` - Remove LRU keys with expiration set
- `allkeys-random` - Remove random keys
- `volatile-ttl` - Remove keys with shortest TTL
- `volatile-random` - Remove random keys with expiration set

---

## Security Best Practices

### 1. Password Protection

```yaml
# docker-compose.yml
services:
  redis:
    command: redis-server --requirepass ${REDIS_PASSWORD}
```

### 2. Network Isolation

```yaml
services:
  redis:
    networks:
      - backend  # Only accessible by backend services
```

### 3. TLS/SSL (Production)

```csharp
var config = ConfigurationOptions.Parse("redis.example.com:6380");
config.Ssl = true;
config.Password = "secure-password";

var redis = ConnectionMultiplexer.Connect(config);
```

---

## Troubleshooting

### Connection Issues

```bash
# Test connection
redis-cli -h localhost -p 6379 -a eshop123 ping

# Check logs
docker logs eshop-redis

# Monitor commands
redis-cli -a eshop123 MONITOR
```

### Memory Issues

```bash
# Check memory usage
redis-cli -a eshop123 INFO memory

# Get biggest keys
redis-cli -a eshop123 --bigkeys

# Clear all data (DANGEROUS!)
redis-cli -a eshop123 FLUSHALL
```

---

## Best Practices

### ✅ DO

1. **Set Expiration** - Always set TTL to prevent memory leaks
2. **Use Appropriate Data Structures** - Hash for objects, Set for unique items
3. **Invalidate Stale Data** - On updates/deletes
4. **Monitor Memory Usage** - Set maxmemory limit
5. **Use Connection Pooling** - IConnectionMultiplexer is thread-safe singleton
6. **Compress Large Values** - > 1KB

### ❌ DON'T

1. **Don't Cache Everything** - Only frequently accessed data
2. **Don't Use Redis as Primary Database** - Use for caching only
3. **Don't Store Sensitive Data Unencrypted**
4. **Don't Use KEYS command in Production** - Use SCAN instead
5. **Don't Block Redis** - Avoid long-running Lua scripts

---

## Наступні кроки

- ✅ [Message Broker (RabbitMQ)](message-broker.md)
- ✅ [Databases (PostgreSQL)](databases.md)
- ✅ [Observability](observability.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
