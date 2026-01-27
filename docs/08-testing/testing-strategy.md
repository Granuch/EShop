# 🧪 Testing Strategy

Загальна стратегія тестування для E-Shop проєкту.

---

## Testing Pyramid

```
        /\
       /  \      E2E Tests (5%)
      / UI \     - Critical user flows
     /------\    - 10-20 scenarios
    /        \   
   / Integ.  \  Integration Tests (25%)
  /  Tests    \ - API endpoints
 /------------\ - Database interactions
/              \
/   Unit Tests  \ Unit Tests (70%)
/________________\ - Domain logic, services
                  - 500+ tests
```

**Coverage Targets**:
- Unit Tests: **> 80%**
- Integration Tests: All critical endpoints
- E2E Tests: Happy paths + critical flows

---

## Testing Levels

### 1. Unit Tests (70% of tests)

**What to test**:
- Domain logic (entities, value objects)
- Business rules
- Validators
- CQRS handlers (mocked dependencies)

**Tools**: xUnit, FluentAssertions, Moq

**Example**:

```csharp
[Fact]
public void Create_WithValidData_ShouldCreateProduct()
{
    // Arrange
    var name = "Test Product";
    var price = new Money(99.99m);
    
    // Act
    var product = Product.Create(name, "Description", "SKU-001", price, 10, Guid.NewGuid(), "system");
    
    // Assert
    product.Should().NotBeNull();
    product.Name.Should().Be(name);
    product.Price.Should().Be(price);
    product.Status.Should().Be(ProductStatus.Draft);
    product.DomainEvents.Should().ContainSingle(e => e is ProductCreatedEvent);
}
```

**Run**:

```bash
dotnet test tests/**/*UnitTests.csproj
```

---

### 2. Integration Tests (25% of tests)

**What to test**:
- API endpoints (end-to-end request/response)
- Database interactions
- External service integration (mocked)

**Tools**: xUnit, WebApplicationFactory, Testcontainers

**Example**:

```csharp
public class ProductsControllerTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;
    
    [Fact]
    public async Task GET_Products_ShouldReturn200WithProducts()
    {
        // Arrange
        var request = "/api/v1/products?page=1&pageSize=20";
        
        // Act
        var response = await _client.GetAsync(request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        content!.Items.Should().NotBeEmpty();
    }
}
```

**Run**:

```bash
dotnet test tests/**/*IntegrationTests.csproj
```

---

### 3. End-to-End Tests (5% of tests)

**What to test**:
- Complete user flows
- Multi-service interactions
- UI + Backend integration

**Tools**: Playwright (React)

**Example**:

```typescript
test('should complete checkout flow', async ({ page }) => {
  // Login
  await page.goto('/login');
  await page.fill('input[name="email"]', 'test@example.com');
  await page.fill('input[name="password"]', 'Test123!');
  await page.click('button[type="submit"]');
  
  // Add product to basket
  await page.goto('/products');
  await page.click('.product-card:first-child button:has-text("Add to Cart")');
  
  // Checkout
  await page.click('a[href="/basket"]');
  await page.click('button:has-text("Checkout")');
  await page.fill('input[name="address"]', '123 Test St');
  await page.click('button:has-text("Place Order")');
  
  // Verify
  await expect(page.locator('h1')).toHaveText('Order Confirmed');
});
```

**Run**:

```bash
npx playwright test
```

---

### 4. Performance Tests

**What to test**:
- Load testing (500+ concurrent users)
- Stress testing (peak load)
- Endurance testing (long-running)

**Tools**: K6

**Example**:

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '1m', target: 50 },
    { duration: '3m', target: 100 },
    { duration: '1m', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const res = http.get('https://api.eshop.com/api/v1/products');
  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
  });
  sleep(1);
}
```

**Run**:

```bash
k6 run tests/performance/load-test.js
```

---

### 5. Security Tests

**What to test**:
- SQL injection
- XSS (Cross-Site Scripting)
- CSRF (Cross-Site Request Forgery)
- Authentication bypass
- Authorization bypass

**Tools**: OWASP ZAP, Burp Suite

**Example**:

```bash
# OWASP ZAP automated scan
docker run -t owasp/zap2docker-stable zap-baseline.py \
  -t https://api.eshop.com \
  -r security-report.html
```

---

## Test Organization

### Folder Structure

```
tests/
├── Catalog.UnitTests/
│   ├── Domain/
│   │   ├── ProductTests.cs
│   │   └── CategoryTests.cs
│   ├── Application/
│   │   ├── Commands/
│   │   │   └── CreateProductCommandHandlerTests.cs
│   │   └── Queries/
│   │       └── GetProductsQueryHandlerTests.cs
│   └── Builders/
│       └── ProductBuilder.cs
│
├── Catalog.IntegrationTests/
│   ├── API/
│   │   └── ProductsControllerTests.cs
│   ├── Infrastructure/
│   │   └── ProductRepositoryTests.cs
│   └── Fixtures/
│       └── CatalogApiFactory.cs
│
├── E2E.Tests/
│   ├── checkout.spec.ts
│   ├── login.spec.ts
│   └── product-search.spec.ts
│
└── Performance.Tests/
    ├── load-test.js
    └── stress-test.js
```

---

## Test Data Management

### 1. Test Builders (Bogus)

```csharp
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
}

// Usage
var product = ProductBuilder.Create();
var products = ProductBuilder.CreateMany(10);
```

---

### 2. Test Fixtures (Shared Context)

```csharp
public class DatabaseFixture : IDisposable
{
    public CatalogDbContext DbContext { get; }
    
    public DatabaseFixture()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("TestDb")
            .Options;
        
        DbContext = new CatalogDbContext(options);
        SeedData();
    }
    
    private void SeedData()
    {
        DbContext.Products.AddRange(ProductBuilder.CreateMany(10));
        DbContext.SaveChanges();
    }
    
    public void Dispose() => DbContext.Dispose();
}

// Usage
public class ProductRepositoryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    
    public ProductRepositoryTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }
}
```

---

### 3. Testcontainers (Real Database)

```csharp
public class IntegrationTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("testdb")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    public CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        
        return new CatalogDbContext(options);
    }
}
```

---

## Mocking Strategies

### When to Mock

✅ **Mock**:
- External APIs (Stripe, SendGrid)
- HTTP calls to other services
- Slow dependencies (database for unit tests)
- Non-deterministic behavior (DateTime.Now, Guid.NewGuid)

❌ **Don't Mock**:
- Domain logic
- Value objects
- DTOs
- Simple classes

### Mocking Examples

```csharp
// Mock repository
public class CreateProductCommandHandlerTests
{
    private readonly Mock<IProductRepository> _repositoryMock;
    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _repositoryMock = new Mock<IProductRepository>();
        _handler = new CreateProductCommandHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateProduct()
    {
        // Arrange
        var command = new CreateProductCommand { Name = "Test", Price = 99.99m };

        _repositoryMock
            .Setup(x => x.GetBySkuAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

---

## Test Naming Conventions

**Format**: `MethodName_Scenario_ExpectedBehavior`

**Examples**:

```csharp
// ✅ Good
[Fact]
public void CreateProduct_WithNegativePrice_ShouldThrowDomainException()

[Fact]
public void UpdateStock_WithZeroQuantity_ShouldMarkAsOutOfStock()

[Fact]
public void Login_WithInvalidCredentials_ShouldReturnUnauthorized()

// ❌ Bad
[Fact]
public void Test1()

[Fact]
public void ProductTest()

[Fact]
public void ShouldThrowException()
```

---

## Assertions (FluentAssertions)

```csharp
// ✅ Fluent syntax (preferred)
product.Name.Should().Be("Test Product");
products.Should().HaveCount(10);
result.IsSuccess.Should().BeTrue();
act.Should().Throw<DomainException>().WithMessage("Price must be positive");

// ❌ xUnit Assert (less readable)
Assert.Equal("Test Product", product.Name);
Assert.Equal(10, products.Count);
Assert.True(result.IsSuccess);
```

---

## CI/CD Integration

### GitHub Actions

```yaml
name: Tests

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: dotnet test tests/**/*UnitTests.csproj --collect:"XPlat Code Coverage"
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

## Code Coverage

**Tool**: Coverlet + Codecov

**Targets**:
- Overall: > 80%
- Domain layer: > 90%
- Application layer: > 80%
- API layer: > 60% (mostly integration tests)

**Reports**:
- Local: `dotnet test --collect:"XPlat Code Coverage"`
- CI/CD: Uploaded to Codecov.io
- Badge: ![Coverage](https://codecov.io/gh/user/repo/branch/main/graph/badge.svg)

---

## Test Execution Time

| Test Type | Count | Duration | Frequency |
|-----------|-------|----------|-----------|
| Unit | 500+ | ~30s | Every commit |
| Integration | 100+ | ~2min | Every PR |
| E2E | 10-20 | ~5min | Before deployment |
| Performance | 5 | ~10min | Weekly |

---

## Best Practices

1. ✅ **AAA Pattern**: Arrange, Act, Assert
2. ✅ **One assertion per test** (or closely related)
3. ✅ **Test edge cases** (null, empty, negative)
4. ✅ **Keep tests fast** (< 100ms per unit test)
5. ✅ **Isolate tests** (no dependencies between tests)
6. ✅ **Use meaningful names** (describe what's being tested)
7. ✅ **Don't test framework code** (EF Core, ASP.NET Core)
8. ✅ **Use test builders** (for complex object creation)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
