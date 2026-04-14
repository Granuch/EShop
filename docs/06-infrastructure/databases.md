# 💾 Databases

PostgreSQL налаштування, migrations, та best practices для мікросервісної архітектури.

---

## Огляд

Кожен мікросервіс має свою окрему базу даних (Database per Service pattern):

| Сервіс | Database | Таблиці | Призначення |
|--------|----------|---------|-------------|
| **Identity** | `identity` | Users, Roles, Claims | User management |
| **Catalog** | `catalog` | Products, Categories, Images | Product catalog |
| **Ordering** | `ordering` | Orders, OrderItems | Order processing |
| **Payment** | `payment` | Payments, Transactions | Payment history |

**Basket Service** використовує **Redis** замість SQL БД.

---

## PostgreSQL Setup

### Docker Compose

```yaml
# deploy/docker/docker-compose.yml

services:
  postgres:
    image: postgres:16-alpine
    container_name: eshop-postgres
    environment:
      POSTGRES_USER: eshop
      POSTGRES_PASSWORD: eshop123
      POSTGRES_DB: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./init-scripts:/docker-entrypoint-initdb.d
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U eshop"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - eshop-network
    command:
      - "postgres"
      - "-c"
      - "max_connections=200"
      - "-c"
      - "shared_buffers=256MB"

volumes:
  postgres_data:

networks:
  eshop-network:
    driver: bridge
```

---

## Database Initialization

### init-scripts/01-create-databases.sql

```sql
-- Create separate databases for each service

-- Identity Service Database
CREATE DATABASE identity;
GRANT ALL PRIVILEGES ON DATABASE identity TO eshop;

-- Catalog Service Database
CREATE DATABASE catalog;
GRANT ALL PRIVILEGES ON DATABASE catalog TO eshop;

-- Ordering Service Database
CREATE DATABASE ordering;
GRANT ALL PRIVILEGES ON DATABASE ordering TO eshop;

-- Payment Service Database (optional)
CREATE DATABASE payment;
GRANT ALL PRIVILEGES ON DATABASE payment TO eshop;
```

---

## Entity Framework Core Configuration

### Connection Strings

**appsettings.json:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=catalog;Username=eshop;Password=eshop123;Include Error Detail=true"
  }
}
```

**appsettings.Production.json:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres;Port=5432;Database=catalog;Username=eshop;Password=${POSTGRES_PASSWORD};SSL Mode=Require;Trust Server Certificate=true"
  }
}
```

---

### DbContext Setup

```csharp
// EShop.Catalog.Infrastructure/Data/CatalogDbContext.cs

public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        // Global query filters (soft delete)
        modelBuilder.Entity<Product>()
            .HasQueryFilter(p => !p.IsDeleted);
    }

    // Domain Events dispatching
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await DispatchDomainEventsAsync();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchDomainEventsAsync()
    {
        var domainEntities = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(x => x.Entity.DomainEvents.Any())
            .Select(x => x.Entity)
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(x => x.DomainEvents)
            .ToList();

        domainEntities.ForEach(entity => entity.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent);
        }
    }
}
```

---

### Entity Configuration (Fluent API)

```csharp
// EShop.Catalog.Infrastructure/Data/Configurations/ProductConfiguration.cs

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.Sku)
            .IsUnique();

        // Value Object mapping (Money)
        builder.OwnsOne(p => p.Price, price =>
        {
            price.Property(m => m.Amount)
                .HasColumnName("price_amount")
                .HasPrecision(18, 2);

            price.Property(m => m.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(3);
        });

        builder.OwnsOne(p => p.DiscountPrice, price =>
        {
            price.Property(m => m.Amount)
                .HasColumnName("discount_price_amount")
                .HasPrecision(18, 2);

            price.Property(m => m.Currency)
                .HasColumnName("discount_price_currency")
                .HasMaxLength(3);
        });

        // Relationships
        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Images)
            .WithOne()
            .HasForeignKey("ProductId")
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes for performance
        builder.HasIndex(p => p.CategoryId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.CreatedAt);

        // Soft delete
        builder.HasQueryFilter(p => !p.IsDeleted);

        // Seed data
        builder.HasData(GetSeedData());
    }

    private static IEnumerable<Product> GetSeedData()
    {
        // Seed data implementation
        return new List<Product>();
    }
}
```

---

## Migrations

### Creating Migrations

```bash
# Add new migration
dotnet ef migrations add InitialCreate \
  -p src/Services/Catalog/EShop.Catalog.Infrastructure \
  -s src/Services/Catalog/EShop.Catalog.API \
  -o Data/Migrations

# Update database
dotnet ef database update \
  -p src/Services/Catalog/EShop.Catalog.Infrastructure \
  -s src/Services/Catalog/EShop.Catalog.API

# Generate SQL script
dotnet ef migrations script \
  -p src/Services/Catalog/EShop.Catalog.Infrastructure \
  -s src/Services/Catalog/EShop.Catalog.API \
  -o migrations.sql
```

---

### Auto Migration on Startup (Development)

```csharp
// Program.cs

var app = builder.Build();

// Auto-apply migrations in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    
    await context.Database.MigrateAsync();
    
    // Seed data
    await CatalogDbContextSeed.SeedAsync(context);
}

app.Run();
```

---

### Manual Migration (Production)

**Для production краще використовувати окремі SQL scripts:**

```bash
# Generate SQL script from last migration to latest
dotnet ef migrations script LastMigration \
  -o Migrations/V2__AddProductImages.sql

# Apply via psql
psql -h localhost -U eshop -d catalog -f Migrations/V2__AddProductImages.sql
```

---

## Seeding Data

```csharp
// EShop.Catalog.Infrastructure/Data/CatalogDbContextSeed.cs

public static class CatalogDbContextSeed
{
    public static async Task SeedAsync(CatalogDbContext context)
    {
        if (await context.Products.AnyAsync())
            return; // Already seeded

        // Seed Categories
        var electronics = Category.Create("Electronics", "Electronic devices");
        var laptops = Category.Create("Laptops", "Laptop computers", electronics.Id);
        
        await context.Categories.AddRangeAsync(electronics, laptops);
        await context.SaveChangesAsync();

        // Seed Products
        var products = new List<Product>
        {
            Product.Create(
                name: "Dell XPS 15",
                description: "High-performance laptop",
                sku: "DELL-XPS15-001",
                price: new Money(1499.99m),
                stockQuantity: 25,
                categoryId: laptops.Id,
                createdBy: "system"
            ),
            Product.Create(
                name: "MacBook Pro 16",
                description: "Apple laptop",
                sku: "APPLE-MBP16-001",
                price: new Money(2499.99m),
                stockQuantity: 15,
                categoryId: laptops.Id,
                createdBy: "system"
            )
        };

        foreach (var product in products)
        {
            product.Publish(); // Change status to Active
        }

        await context.Products.AddRangeAsync(products);
        await context.SaveChangesAsync();
    }
}
```

---

## Performance Optimization

### Indexing Strategy

```csharp
// Add indexes in OnModelCreating

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Single column indexes
    modelBuilder.Entity<Product>()
        .HasIndex(p => p.Sku)
        .IsUnique();

    modelBuilder.Entity<Product>()
        .HasIndex(p => p.CategoryId);

    // Composite indexes (for common queries)
    modelBuilder.Entity<Product>()
        .HasIndex(p => new { p.CategoryId, p.Status });

    // Full-text search index (PostgreSQL specific)
    modelBuilder.Entity<Product>()
        .HasIndex(p => new { p.Name, p.Description })
        .HasMethod("GIN")
        .IsTsVectorExpressionIndex("english");
}
```

---

### Pagination Best Practices

**❌ Bad - Skip/Take:**
```csharp
// Slow for large datasets
var products = await context.Products
    .OrderBy(p => p.Id)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

**✅ Good - Keyset Pagination:**
```csharp
// Fast for large datasets
var products = await context.Products
    .Where(p => p.Id > lastSeenId)
    .OrderBy(p => p.Id)
    .Take(pageSize)
    .ToListAsync();
```

---

### Query Splitting

```csharp
// For queries with multiple collections (avoid cartesian explosion)
var products = await context.Products
    .Include(p => p.Images)
    .Include(p => p.Attributes)
    .AsSplitQuery() // Executes separate queries
    .ToListAsync();
```

---

### No Tracking for Read-Only Queries

```csharp
// Read-only queries
var products = await context.Products
    .AsNoTracking() // Faster, no change tracking
    .Where(p => p.CategoryId == categoryId)
    .ToListAsync();
```

---

## Outbox Pattern (Transactional Messaging)

Забезпечує атомарність між зміною БД та publishing events.

```csharp
// EShop.BuildingBlocks.Domain/OutboxMessage.cs

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredOn { get; set; }
    public DateTime? ProcessedOn { get; set; }
    public string? Error { get; set; }
}
```

```csharp
// Save domain event to Outbox instead of publishing immediately

public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var domainEvents = ChangeTracker
        .Entries<AggregateRoot>()
        .SelectMany(x => x.Entity.DomainEvents)
        .ToList();

    // Save events to Outbox table
    foreach (var domainEvent in domainEvents)
    {
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = domainEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(domainEvent),
            OccurredOn = DateTime.UtcNow
        };

        OutboxMessages.Add(outboxMessage);
    }

    return await base.SaveChangesAsync(ct);
}
```

**Background Worker** читає Outbox та публікує events:

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
                    await PublishEventAsync(message);
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

## Connection Pooling

**ASP.NET Core автоматично використовує connection pooling**, але можна налаштувати:

```csharp
builder.Services.AddDbContext<CatalogDbContext>(options =>
{
    options.UseNpgsql(
        connectionString,
        npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);

            // Command timeout
            npgsqlOptions.CommandTimeout(30);

            // Migrations assembly
            npgsqlOptions.MigrationsAssembly(
                typeof(CatalogDbContext).Assembly.FullName);
        });

    // Connection pooling (default is enabled)
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});
```

---

## Backup & Restore

### Backup

```bash
# Full backup
pg_dump -h localhost -U eshop -d catalog -F c -f catalog_backup.dump

# Backup with compression
pg_dump -h localhost -U eshop -d catalog -F c -Z 9 -f catalog_backup.dump

# Backup schema only
pg_dump -h localhost -U eshop -d catalog --schema-only -f schema.sql

# Backup data only
pg_dump -h localhost -U eshop -d catalog --data-only -f data.sql
```

### Restore

```bash
# Restore from dump
pg_restore -h localhost -U eshop -d catalog catalog_backup.dump

# Restore from SQL
psql -h localhost -U eshop -d catalog -f backup.sql
```

---

## Monitoring Queries

### Slow Query Logging

**postgresql.conf:**
```conf
log_min_duration_statement = 1000  # Log queries > 1 second
log_statement = 'all'
```

### EF Core Query Logging

```csharp
builder.Services.AddDbContext<CatalogDbContext>(options =>
{
    options.UseNpgsql(connectionString)
           .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
           .EnableDetailedErrors(builder.Environment.IsDevelopment())
           .LogTo(Console.WriteLine, LogLevel.Information);
});
```

---

## Multi-tenancy Considerations

Якщо потрібна підтримка кількох клієнтів:

**Option 1: Database per Tenant**
```csharp
var connectionString = $"Host=localhost;Database=catalog_{tenantId};...";
```

**Option 2: Schema per Tenant**
```csharp
modelBuilder.HasDefaultSchema($"tenant_{tenantId}");
```

**Option 3: Row-level isolation**
```csharp
modelBuilder.Entity<Product>()
    .HasQueryFilter(p => p.TenantId == currentTenantId);
```

---

## Database Schema Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    Catalog Database                      │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌─────────────┐        ┌──────────────┐               │
│  │ categories  │◄───────│  products    │               │
│  │─────────────│ 1    * │──────────────│               │
│  │ id (PK)     │        │ id (PK)      │               │
│  │ name        │        │ name         │               │
│  │ slug        │        │ sku (UK)     │               │
│  │ parent_id   │        │ category_id  │               │
│  └─────────────┘        │ price_amount │               │
│                         │ price_currency│              │
│                         │ stock_quantity│              │
│                         └───────┬───────┘              │
│                                 │                       │
│                                 │ 1                     │
│                                 │                       │
│                                 │ *                     │
│                         ┌───────▼────────┐             │
│                         │ product_images │             │
│                         │────────────────│             │
│                         │ id (PK)        │             │
│                         │ product_id (FK)│             │
│                         │ url            │             │
│                         │ alt_text       │             │
│                         │ display_order  │             │
│                         └────────────────┘             │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

---

## Best Practices

### ✅ DO

1. **Use Database per Service** - Кожен сервіс має свою БД
2. **Use Migrations** - Версіонування схеми через EF Core migrations
3. **Use Indexes** - На foreign keys та часто використовуваних колонках
4. **Use Transactions** - Для atomic operations
5. **Use Connection Pooling** - Автоматично в EF Core
6. **Monitor Slow Queries** - Через PostgreSQL logs
7. **Backup Regularly** - Автоматичні backups щодня

### ❌ DON'T

1. **Don't Share Databases** - Між сервісами
2. **Don't Use SELECT *** - Вибирайте тільки потрібні колонки
3. **Don't Track Read-Only Queries** - Використовуйте AsNoTracking()
4. **Don't Hardcode Passwords** - Використовуйте secrets/environment variables
5. **Don't Apply Migrations in Production Automatically** - Тільки через CI/CD

---

## Security

### Connection String Security

**❌ Bad:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Password=password123"
  }
}
```

**✅ Good (Development):**
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Password=..."
```

**✅ Good (Production):**
```bash
export ConnectionStrings__DefaultConnection="Host=postgres;Password=${DB_PASSWORD}"
```

### Row-Level Security (RLS)

PostgreSQL підтримує RLS для додаткової безпеки:

```sql
ALTER TABLE products ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON products
    USING (tenant_id = current_setting('app.current_tenant')::uuid);
```

---

## Troubleshooting

### Connection Issues

```bash
# Check PostgreSQL is running
docker ps | grep postgres

# Check logs
docker logs eshop-postgres

# Connect manually
docker exec -it eshop-postgres psql -U eshop -d catalog
```

### Migration Issues

```bash
# Remove last migration
dotnet ef migrations remove

# List all migrations
dotnet ef migrations list

# Check pending migrations
dotnet ef migrations has-pending-model-changes
```

---

## Наступні кроки

- ✅ [Caching (Redis)](caching.md)
- ✅ [Message Broker (RabbitMQ)](message-broker.md)
- ✅ [Observability](observability.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
