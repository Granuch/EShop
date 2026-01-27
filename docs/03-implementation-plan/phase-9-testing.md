# 🧪 Phase 9: Testing Strategy Implementation

**Duration**: 2 weeks  
**Team Size**: 2-3 developers  
**Prerequisites**: All phases 1-8 completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Unit tests for all services (> 80% coverage)
- ✅ Integration tests for APIs
- ✅ End-to-end tests for critical flows
- ✅ Performance tests
- ✅ Security tests
- ✅ Load tests

---

## Testing Pyramid

```
        /\
       /  \      E2E Tests (10%)
      /    \     - Critical user flows
     /------\    
    /        \   Integration Tests (30%)
   /          \  - API endpoints
  /------------\ 
 /              \ Unit Tests (60%)
/________________\ - Domain logic, services
```

---

## Tasks Breakdown

### 9.1 Unit Tests Setup

**Estimated Time**: 3 days

**NuGet Packages:**

```xml
<ItemGroup>
  <PackageReference Include="xUnit" Version="2.6.2" />
  <PackageReference Include="xUnit.runner.visualstudio" Version="2.5.4" />
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="Bogus" Version="35.0.1" />
</ItemGroup>
```

**Unit Test Example - Domain:**

```csharp
// tests/Catalog.Tests/Domain/ProductTests.cs

public class ProductTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreateProduct()
    {
        // Arrange
        var name = "Test Product";
        var sku = "TEST-001";
        var price = new Money(99.99m);

        // Act
        var product = Product.Create(
            name, "Description", sku, price, 10, Guid.NewGuid(), "system");

        // Assert
        product.Should().NotBeNull();
        product.Name.Should().Be(name);
        product.Sku.Should().Be(sku);
        product.Price.Should().Be(price);
        product.Status.Should().Be(ProductStatus.Draft);
        product.DomainEvents.Should().ContainSingle(e => e is ProductCreatedEvent);
    }

    [Fact]
    public void UpdatePrice_WithNegativeAmount_ShouldThrowDomainException()
    {
        // Arrange
        var product = ProductFactory.Create();
        var invalidPrice = new Money(-10);

        // Act & Assert
        var act = () => product.UpdatePrice(invalidPrice);
        act.Should().Throw<DomainException>()
            .WithMessage("Price must be greater than zero");
    }

    [Fact]
    public void Publish_WithZeroStock_ShouldThrowDomainException()
    {
        // Arrange
        var product = ProductFactory.Create();
        product.UpdateStock(0);

        // Act & Assert
        var act = () => product.Publish();
        act.Should().Throw<DomainException>()
            .WithMessage("Cannot publish product with zero stock");
    }
}
```

**Unit Test Example - Application (CQRS):**

```csharp
// tests/Catalog.Tests/Application/CreateProductCommandHandlerTests.cs

public class CreateProductCommandHandlerTests
{
    private readonly Mock<IProductRepository> _productRepositoryMock;
    private readonly Mock<ICategoryRepository> _categoryRepositoryMock;
    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _productRepositoryMock = new Mock<IProductRepository>();
        _categoryRepositoryMock = new Mock<ICategoryRepository>();
        _handler = new CreateProductCommandHandler(
            _productRepositoryMock.Object,
            _categoryRepositoryMock.Object,
            Mock.Of<ILogger<CreateProductCommandHandler>>());
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateProduct()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var command = new CreateProductCommand
        {
            Name = "Test Product",
            Sku = "TEST-001",
            Price = 99.99m,
            StockQuantity = 10,
            CategoryId = categoryId
        };

        _categoryRepositoryMock
            .Setup(x => x.GetByIdAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CategoryFactory.Create(categoryId));

        _productRepositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _productRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithDuplicateSku_ShouldReturnFailure()
    {
        // Arrange
        var command = new CreateProductCommand { Sku = "DUPLICATE-SKU" };

        _productRepositoryMock
            .Setup(x => x.GetBySkuAsync(command.Sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProductFactory.Create());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("SKU_EXISTS");
    }
}
```

---

### 9.2 Integration Tests

**Estimated Time**: 3 days

**Setup:**

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
    }
}
```

**Integration Test Example:**

```csharp
// tests/Catalog.IntegrationTests/ProductsControllerTests.cs

public class ProductsControllerTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;

    public ProductsControllerTests(CatalogApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_Products_ShouldReturn200WithProducts()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/products?page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        content.Should().NotBeNull();
        content!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task POST_Products_WithValidData_ShouldReturn201()
    {
        // Arrange
        var request = new CreateProductRequest
        {
            Name = "New Product",
            Sku = "NEW-001",
            Price = 99.99m,
            StockQuantity = 10,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task POST_Products_WithDuplicateSku_ShouldReturn400()
    {
        // Arrange
        var request = new CreateProductRequest { Sku = "DUPLICATE-SKU" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

---

### 9.3 End-to-End Tests (Playwright)

**Estimated Time**: 2 days

**Setup:**

```bash
npm install -D @playwright/test
npx playwright install
```

**E2E Test Example:**

```typescript
// tests/e2e/checkout.spec.ts

import { test, expect } from '@playwright/test';

test.describe('Checkout Flow', () => {
  test('should complete checkout successfully', async ({ page }) => {
    // Login
    await page.goto('/login');
    await page.fill('input[name="email"]', 'test@test.com');
    await page.fill('input[name="password"]', 'Test123!');
    await page.click('button[type="submit"]');
    
    // Browse products
    await page.goto('/products');
    await page.waitForSelector('.product-card');
    
    // Add product to basket
    await page.click('.product-card:first-child button:has-text("Add to Cart")');
    await expect(page.locator('.basket-count')).toHaveText('1');
    
    // Go to basket
    await page.click('a[href="/basket"]');
    await expect(page.locator('.basket-item')).toHaveCount(1);
    
    // Proceed to checkout
    await page.click('button:has-text("Proceed to Checkout")');
    
    // Fill checkout form
    await page.fill('input[name="address"]', '123 Test St');
    await page.selectOption('select[name="paymentMethod"]', 'credit_card');
    
    // Place order
    await page.click('button:has-text("Place Order")');
    
    // Verify order confirmation
    await expect(page.locator('h1')).toHaveText('Order Confirmed');
    await expect(page.locator('.order-number')).toBeVisible();
  });

  test('should handle out-of-stock products', async ({ page }) => {
    await page.goto('/products');
    
    const outOfStockProduct = page.locator('.product-card:has-text("Out of Stock")');
    await expect(outOfStockProduct.locator('button')).toBeDisabled();
  });
});
```

---

### 9.4 Performance Tests (K6)

**Estimated Time**: 2 days

**Setup:**

```bash
# Install K6
brew install k6  # macOS
# or
choco install k6  # Windows
```

**Load Test Script:**

```javascript
// tests/performance/load-test.js

import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '1m', target: 50 },   // Ramp up to 50 users
    { duration: '3m', target: 50 },   // Stay at 50 users
    { duration: '1m', target: 100 },  // Ramp up to 100 users
    { duration: '3m', target: 100 },  // Stay at 100 users
    { duration: '1m', target: 0 },    // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% of requests must complete below 500ms
    http_req_failed: ['rate<0.01'],   // Less than 1% error rate
  },
};

export default function () {
  // Get products
  let res = http.get('http://localhost:5000/api/v1/products');
  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
  });

  sleep(1);

  // Get specific product
  res = http.get('http://localhost:5000/api/v1/products/some-id');
  check(res, {
    'product detail status is 200': (r) => r.status === 200,
  });

  sleep(1);
}
```

**Run:**

```bash
k6 run tests/performance/load-test.js
```

---

### 9.5 Security Tests

**Estimated Time**: 2 days

**OWASP ZAP Automated Scan:**

```bash
# Pull ZAP Docker image
docker pull owasp/zap2docker-stable

# Run automated scan
docker run -t owasp/zap2docker-stable zap-baseline.py \
  -t http://localhost:5000 \
  -r zap-report.html
```

**Security Test Checklist:**

- [x] SQL Injection testing
- [x] XSS (Cross-Site Scripting) testing
- [x] CSRF (Cross-Site Request Forgery) protection
- [x] Authentication bypass attempts
- [x] Authorization checks
- [x] Sensitive data exposure
- [x] Rate limiting verification
- [x] JWT token validation

**Manual Security Test Example:**

```csharp
[Fact]
public async Task UnauthorizedUser_CannotAccessProtectedEndpoint()
{
    // Act
    var response = await _client.GetAsync("/api/v1/orders");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}

[Fact]
public async Task User_CannotAccessOtherUsersOrders()
{
    // Arrange
    var userAToken = await GetUserToken("userA@test.com");
    var userBOrderId = await CreateOrderForUser("userB@test.com");

    _client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", userAToken);

    // Act
    var response = await _client.GetAsync($"/api/v1/orders/{userBOrderId}");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

---

### 9.6 Test Data Management

**Estimated Time**: 1 day

**Test Data Builders (Bogus):**

```csharp
// tests/Shared/Builders/ProductBuilder.cs

public class ProductBuilder
{
    private static readonly Faker<Product> Faker = new Faker<Product>()
        .CustomInstantiator(f => Product.Create(
            name: f.Commerce.ProductName(),
            description: f.Lorem.Paragraph(),
            sku: f.Random.AlphaNumeric(10),
            price: new Money(f.Random.Decimal(10, 1000)),
            stockQuantity: f.Random.Int(0, 100),
            categoryId: Guid.NewGuid(),
            createdBy: "system"
        ));

    public static Product Create() => Faker.Generate();

    public static List<Product> CreateMany(int count) => Faker.Generate(count);

    public static Product CreateWithNoStock() => Faker
        .RuleFor(p => p.StockQuantity, 0)
        .Generate();
}
```

---

## CI/CD Integration

**GitHub Actions Workflow:**

```yaml
# .github/workflows/test.yml

name: Tests

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: dotnet test --collect:"XPlat Code Coverage"
      - uses: codecov/codecov-action@v3

  integration-tests:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16
      redis:
        image: redis:7
    steps:
      - run: dotnet test tests/**/*IntegrationTests.csproj

  e2e-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/setup-node@v4
      - run: npm install
      - run: npx playwright test
```

---

## Success Criteria

- [x] Unit test coverage > 80%
- [x] All integration tests passing
- [x] Critical user flows covered by E2E tests
- [x] Performance benchmarks met (p95 < 500ms)
- [x] Security scan passes with no high-severity issues
- [x] All tests automated in CI/CD

---

## Next Phase

→ [Phase 10: DevOps & Deployment](phase-10-devops.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
