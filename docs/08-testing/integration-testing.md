# 🔗 Integration Testing Guide

Integration tests для API endpoints та database interactions.

---

## What to Integration Test

✅ **Test**:
- HTTP endpoints (Controllers)
- Database queries (Repository)
- EF Core mappings
- Authentication/Authorization
- Middleware
- Complete request/response flow

❌ **Don't Test**:
- Business logic (use unit tests)
- UI rendering (use E2E tests)

---

## Test Setup

### WebApplicationFactory

```csharp
// tests/Catalog.IntegrationTests/CatalogApiFactory.cs

public class CatalogApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real database
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<CatalogDbContext>));
            
            if (descriptor != null)
                services.Remove(descriptor);

            // Add in-memory database
            services.AddDbContext<CatalogDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestDb");
            });

            // Seed test data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.Database.EnsureCreated();
            SeedTestData(db);
        });

        builder.UseEnvironment("Testing");
    }

    private static void SeedTestData(CatalogDbContext context)
    {
        // Add test categories
        var electronics = Category.Create("Electronics", "system");
        var clothing = Category.Create("Clothing", "system");
        context.Categories.AddRange(electronics, clothing);

        // Add test products
        var products = ProductBuilder.CreateMany(10);
        foreach (var product in products)
        {
            product.Publish(); // Make them visible
        }
        context.Products.AddRange(products);

        context.SaveChanges();
    }
}
```

---

## Controller Tests

### Example: ProductsController

```csharp
[TestFixture]
public class ProductsControllerTests
{
    private CatalogApiFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new CatalogApiFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task GET_Products_ShouldReturn200WithProducts()
    {
        // Arrange
        var url = "/api/v1/products?page=1&pageSize=10";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        content.Should().NotBeNull();
        content!.Items.Should().NotBeEmpty();
        content.Items.Should().HaveCountLessOrEqualTo(10);
        content.TotalCount.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task GET_ProductById_WithValidId_ShouldReturn200()
    {
        // Arrange
        var productId = await GetFirstProductIdAsync();
        var url = $"/api/v1/products/{productId}";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        product.Should().NotBeNull();
        product!.Id.Should().Be(productId);
    }

    [Test]
    public async Task GET_ProductById_WithInvalidId_ShouldReturn404()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var url = $"/api/v1/products/{nonExistentId}";

        // Act
        var response = await _client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task POST_Products_WithValidData_ShouldReturn201()
    {
        // Arrange
        var request = new CreateProductRequest
        {
            Name = "New Product",
            Description = "Description",
            Sku = "NEW-001",
            Price = 99.99m,
            StockQuantity = 10,
            CategoryId = await GetFirstCategoryIdAsync()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        
        var productId = await response.Content.ReadFromJsonAsync<Guid>();
        productId.Should().NotBeEmpty();

        // Verify product was created
        var getResponse = await _client.GetAsync(response.Headers.Location);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task POST_Products_WithDuplicateSku_ShouldReturn400()
    {
        // Arrange
        var existingSku = await GetFirstProductSkuAsync();
        var request = new CreateProductRequest
        {
            Name = "Product",
            Sku = existingSku, // Duplicate!
            Price = 99.99m,
            StockQuantity = 10,
            CategoryId = await GetFirstCategoryIdAsync()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Code.Should().Be("SKU_EXISTS");
    }

    [Test]
    public async Task PUT_Products_WithValidData_ShouldReturn204()
    {
        // Arrange
        var productId = await GetFirstProductIdAsync();
        var request = new UpdateProductRequest
        {
            Name = "Updated Name",
            Description = "Updated Description",
            Price = 199.99m
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/v1/products/{productId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify update
        var getResponse = await _client.GetAsync($"/api/v1/products/{productId}");
        var product = await getResponse.Content.ReadFromJsonAsync<ProductDto>();
        product!.Name.Should().Be(request.Name);
        product.Price.Should().Be(request.Price);
    }

    [Test]
    public async Task DELETE_Products_WithValidId_ShouldReturn204()
    {
        // Arrange
        var productId = await GetFirstProductIdAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/products/{productId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/v1/products/{productId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Helper methods
    private async Task<Guid> GetFirstProductIdAsync()
    {
        var response = await _client.GetAsync("/api/v1/products?page=1&pageSize=1");
        var content = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        return content!.Items.First().Id;
    }

    private async Task<string> GetFirstProductSkuAsync()
    {
        var response = await _client.GetAsync("/api/v1/products?page=1&pageSize=1");
        var content = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        return content!.Items.First().Sku;
    }

    private async Task<Guid> GetFirstCategoryIdAsync()
    {
        var response = await _client.GetAsync("/api/v1/categories");
        var content = await response.Content.ReadFromJsonAsync<List<CategoryDto>>();
        return content!.First().Id;
    }
}
```

---

## Authentication Tests

### Setup Authentication

```csharp
public class AuthenticatedApiFactory : CatalogApiFactory
{
    public HttpClient CreateAuthenticatedClient(string role = "User")
    {
        var client = CreateClient();
        
        // Generate test JWT token
        var token = GenerateTestToken(role);
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
        
        return client;
    }

    private string GenerateTestToken(string role)
    {
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("email", "test@example.com"),
            new Claim(ClaimTypes.Role, role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-at-least-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "TestIssuer",
            audience: "TestAudience",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

### Authorization Tests

```csharp
[TestFixture]
public class ProductsControllerAuthTests
{
    private AuthenticatedApiFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new AuthenticatedApiFactory();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task POST_Products_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        var client = _factory.CreateClient(); // No auth
        var request = new CreateProductRequest { Name = "Test" };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task POST_Products_WithUserRole_ShouldReturn403()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient("User"); // Not Admin
        var request = new CreateProductRequest { Name = "Test", Price = 99.99m };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task POST_Products_WithAdminRole_ShouldReturn201()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient("Admin");
        var request = new CreateProductRequest
        {
            Name = "Test Product",
            Sku = "TEST-001",
            Price = 99.99m,
            StockQuantity = 10,
            CategoryId = await GetFirstCategoryIdAsync(client)
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

---

## Repository Tests (Testcontainers)

### Setup Testcontainers

```csharp
[TestFixture]
public class IntegrationTestFixture
{
    private PostgreSqlContainer _postgres = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("testdb")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
            
        await _postgres.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _postgres.DisposeAsync();
    }

    public CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var context = new CatalogDbContext(options);
        context.Database.Migrate(); // Apply migrations
        return context;
    }
}
```

### Repository Integration Tests

```csharp
public class ProductRepositoryTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;

    public ProductRepositoryTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_ShouldPersistProduct()
    {
        // Arrange
        await using var context = _fixture.CreateDbContext();
        var repository = new ProductRepository(context);
        var product = ProductBuilder.Create();

        // Act
        await repository.AddAsync(product);
        await repository.SaveChangesAsync();

        // Assert (verify in database)
        var savedProduct = await context.Products.FindAsync(product.Id);
        savedProduct.Should().NotBeNull();
        savedProduct!.Name.Should().Be(product.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WithValidId_ShouldReturnProduct()
    {
        // Arrange
        await using var context = _fixture.CreateDbContext();
        var repository = new ProductRepository(context);
        
        var product = ProductBuilder.Create();
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.GetByIdAsync(product.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(product.Id);
        result.Category.Should().NotBeNull(); // Eager loaded
    }

    [Fact]
    public async Task GetBySkuAsync_WithExistingSku_ShouldReturnProduct()
    {
        // Arrange
        await using var context = _fixture.CreateDbContext();
        var repository = new ProductRepository(context);
        
        var product = ProductBuilder.Create();
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Act
        var result = await repository.GetBySkuAsync(product.Sku);

        // Assert
        result.Should().NotBeNull();
        result!.Sku.Should().Be(product.Sku);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistChanges()
    {
        // Arrange
        await using var context = _fixture.CreateDbContext();
        var repository = new ProductRepository(context);
        
        var product = ProductBuilder.Create();
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Act
        product.UpdatePrice(new Money(199.99m));
        await repository.UpdateAsync(product);
        await repository.SaveChangesAsync();

        // Assert
        var updated = await context.Products.FindAsync(product.Id);
        updated!.Price.Amount.Should().Be(199.99m);
    }
}
```

---

## Testing Pagination

```csharp
[Fact]
public async Task GET_Products_WithPagination_ShouldReturnCorrectPage()
{
    // Arrange
    var page = 2;
    var pageSize = 5;

    // Act
    var response = await _client.GetAsync($"/api/v1/products?page={page}&pageSize={pageSize}");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    
    var content = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
    content!.Items.Should().HaveCount(pageSize);
    content.Page.Should().Be(page);
    content.PageSize.Should().Be(pageSize);
}
```

---

## Testing Filtering

```csharp
[Fact]
public async Task GET_Products_WithCategoryFilter_ShouldReturnOnlyThatCategory()
{
    // Arrange
    var categoryId = await GetFirstCategoryIdAsync();

    // Act
    var response = await _client.GetAsync($"/api/v1/products?categoryId={categoryId}");

    // Assert
    var content = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
    content!.Items.Should().OnlyContain(p => p.CategoryId == categoryId);
}

[Theory]
[InlineData(0, 100)]
[InlineData(50, 150)]
public async Task GET_Products_WithPriceFilter_ShouldReturnCorrectProducts(decimal min, decimal max)
{
    // Act
    var response = await _client.GetAsync($"/api/v1/products?minPrice={min}&maxPrice={max}");

    // Assert
    var content = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
    content!.Items.Should().OnlyContain(p => p.Price >= min && p.Price <= max);
}
```

---

## Testing Validation

```csharp
[Fact]
public async Task POST_Products_WithMissingName_ShouldReturn400WithValidationError()
{
    // Arrange
    var request = new CreateProductRequest
    {
        Name = "", // Missing!
        Price = 99.99m
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/v1/products", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    
    var error = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>();
    error!.Errors.Should().ContainKey("Name");
}
```

---

## Testing Rate Limiting

```csharp
[Fact]
public async Task POST_Products_ExceedingRateLimit_ShouldReturn429()
{
    // Arrange
    var client = _factory.CreateAuthenticatedClient("Admin");

    // Act - Make 101 requests (limit is 100/minute)
    for (int i = 0; i < 101; i++)
    {
        var response = await client.GetAsync("/api/v1/products");
        
        if (i == 100)
        {
            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            response.Headers.Should().ContainKey("Retry-After");
        }
    }
}
```

---

## Best Practices

✅ **Do**:
- Use WebApplicationFactory for API tests
- Use Testcontainers for real database tests
- Test complete request/response flow
- Test authentication and authorization
- Test error responses (400, 404, 500)
- Clean database between tests (or use separate databases)

❌ **Don't**:
- Share state between tests
- Use production database
- Test business logic (use unit tests)
- Ignore slow tests (> 1s per test)

---

## Running Integration Tests

```bash
# Run all integration tests
dotnet test tests/**/*IntegrationTests.csproj

# Run with Docker (for Testcontainers)
docker run -d -p 5432:5432 postgres:16

# Run in CI/CD (GitHub Actions)
services:
  postgres:
    image: postgres:16
    env:
      POSTGRES_PASSWORD: test
```

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
