# 🤝 Team Agreement

Правила та стандарти роботи команди над проектом E-Shop Microservices.

---

## 📋 Зміст

1. [Coding Conventions](#coding-conventions)
2. [Git Workflow](#git-workflow)
3. [Pull Request Process](#pull-request-process)
4. [Code Review Guidelines](#code-review-guidelines)
5. [Definition of Done](#definition-of-done)
6. [Communication Channels](#communication-channels)
7. [Meeting Schedule](#meeting-schedule)
8. [Incident Response](#incident-response)

---

## Coding Conventions

### C# / .NET

#### Naming Conventions

```csharp
// ✅ PascalCase для класів, методів, властивостей
public class ProductService { }
public void GetProductById(int id) { }
public string ProductName { get; set; }

// ✅ camelCase для параметрів, локальних змінних
public void ProcessOrder(int orderId)
{
    var orderItems = GetOrderItems(orderId);
}

// ✅ _camelCase для private fields
private readonly ILogger<ProductService> _logger;
private int _retryCount;

// ✅ IPascalCase для інтерфейсів
public interface IProductRepository { }

// ✅ UPPER_CASE для констант
public const int MAX_RETRY_ATTEMPTS = 3;

// ❌ Уникайте скорочень
// Bad
public void ProcOrd(int oid) { }

// Good
public void ProcessOrder(int orderId) { }
```

#### Code Organization

```csharp
// ✅ Порядок членів класу:
public class ProductService
{
    // 1. Constants
    private const int DefaultPageSize = 10;
    
    // 2. Static fields
    private static readonly string DefaultCulture = "en-US";
    
    // 3. Fields
    private readonly IProductRepository _repository;
    private readonly ILogger<ProductService> _logger;
    
    // 4. Constructor
    public ProductService(IProductRepository repository, ILogger<ProductService> logger)
    {
        _repository = repository;
        _logger = logger;
    }
    
    // 5. Properties
    public int MaxRetries { get; set; }
    
    // 6. Public methods
    public async Task<Product> GetProductByIdAsync(int id)
    {
        // ...
    }
    
    // 7. Private methods
    private bool ValidateProduct(Product product)
    {
        // ...
    }
}
```

#### Async/Await

```csharp
// ✅ Завжди використовуйте async/await для I/O операцій
public async Task<Product> GetProductAsync(int id)
{
    return await _repository.GetByIdAsync(id);
}

// ✅ Суфікс Async для async методів
public async Task<IEnumerable<Product>> GetProductsAsync()
{
    return await _repository.GetAllAsync();
}

// ❌ Уникайте async void (тільки для event handlers)
// Bad
public async void ProcessOrder() { }

// Good
public async Task ProcessOrderAsync() { }
```

#### Dependency Injection

```csharp
// ✅ Constructor injection (рекомендовано)
public class ProductService
{
    private readonly IProductRepository _repository;
    
    public ProductService(IProductRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }
}

// ❌ Уникайте Service Locator pattern
```

#### LINQ

```csharp
// ✅ Query syntax для складних запитів
var products = from p in _context.Products
               where p.Price > 100
               orderby p.Name
               select new ProductDto
               {
                   Id = p.Id,
                   Name = p.Name
               };

// ✅ Method syntax для простих операцій
var activeProducts = products.Where(p => p.IsActive).ToList();

// ✅ Уникайте ToList() без потреби (lazy evaluation)
```

---

### TypeScript / React

#### Naming Conventions

```typescript
// ✅ PascalCase для компонентів, типів, інтерфейсів
interface Product {
  id: number;
  name: string;
}

type ProductFilter = 'all' | 'active' | 'archived';

export const ProductList: React.FC = () => { };

// ✅ camelCase для змінних, функцій
const productService = new ProductService();
const fetchProducts = async () => { };

// ✅ UPPER_SNAKE_CASE для констант
const API_BASE_URL = 'http://localhost:5000';
const MAX_RETRIES = 3;
```

#### React Component Structure

```tsx
// ✅ Functional components з TypeScript
interface ProductCardProps {
  product: Product;
  onAddToCart: (productId: number) => void;
}

export const ProductCard: React.FC<ProductCardProps> = ({ product, onAddToCart }) => {
  // 1. Hooks
  const [isLoading, setIsLoading] = useState(false);
  
  // 2. Event handlers
  const handleAddToCart = () => {
    setIsLoading(true);
    onAddToCart(product.id);
  };
  
  // 3. Render
  return (
    <div className="product-card">
      <h3>{product.name}</h3>
      <button onClick={handleAddToCart} disabled={isLoading}>
        Add to Cart
      </button>
    </div>
  );
};
```

#### Custom Hooks

```typescript
// ✅ Префікс 'use' для custom hooks
export const useProducts = () => {
  const [products, setProducts] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);
  
  useEffect(() => {
    fetchProducts();
  }, []);
  
  return { products, loading };
};
```

---

### SQL

```sql
-- ✅ UPPERCASE для SQL keywords
SELECT p.Id, p.Name, p.Price
FROM Products p
WHERE p.IsActive = true
ORDER BY p.CreatedAt DESC;

-- ✅ PascalCase для таблиць та колонок (EF Core convention)
CREATE TABLE Products (
    Id INT PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    Price DECIMAL(18,2) NOT NULL
);

-- ✅ Використовуйте table aliases
SELECT p.Name, c.Name AS CategoryName
FROM Products p
INNER JOIN Categories c ON p.CategoryId = c.Id;
```

---

### Code Comments

```csharp
// ✅ XML comments для public API
/// <summary>
/// Gets a product by its unique identifier.
/// </summary>
/// <param name="id">The product identifier.</param>
/// <returns>The product if found; otherwise, null.</returns>
public async Task<Product?> GetProductByIdAsync(int id)
{
    // Implementation comment тільки якщо логіка складна
    return await _repository.GetByIdAsync(id);
}

// ❌ Уникайте очевидних коментарів
// Bad
// Increment counter
counter++;

// Good (коли потрібно пояснення)
// Retry 3 times with exponential backoff to handle transient failures
for (int i = 0; i < 3; i++) { }
```

---

## Git Workflow

### Branch Naming

```bash
# ✅ Feature branches
git checkout -b feature/catalog-search
git checkout -b feature/user-authentication

# ✅ Bugfix branches
git checkout -b bugfix/cart-total-calculation
git checkout -b bugfix/order-status-update

# ✅ Hotfix branches (для production)
git checkout -b hotfix/payment-gateway-error

# ✅ Chore/refactor
git checkout -b chore/update-dependencies
git checkout -b refactor/extract-order-service
```

### Commit Messages

Використовуємо [Conventional Commits](https://www.conventionalcommits.org/):

```bash
# Формат:
# <type>(<scope>): <subject>
#
# <body> (optional)
#
# <footer> (optional)

# ✅ Приклади
git commit -m "feat(catalog): add product search with filters"
git commit -m "fix(basket): correct total price calculation"
git commit -m "docs(readme): update setup instructions"
git commit -m "refactor(ordering): extract payment processing logic"
git commit -m "test(catalog): add integration tests for product API"
git commit -m "chore(deps): update MassTransit to 8.1.0"

# Types:
# - feat: New feature
# - fix: Bug fix
# - docs: Documentation only
# - style: Code style changes (formatting, semicolons, etc)
# - refactor: Code refactoring
# - test: Adding tests
# - chore: Maintenance tasks
# - perf: Performance improvements
# - ci: CI/CD changes

# ✅ Приклад з body
git commit -m "feat(payment): integrate Stripe payment gateway

Implemented Stripe payment processing with:
- Card payment support
- Webhook handling for async notifications
- Retry logic for failed payments

Closes #123"
```

### Pull Request Workflow

1. **Створіть feature branch** від `main`
   ```bash
   git checkout main
   git pull origin main
   git checkout -b feature/your-feature
   ```

2. **Робіть часті commits** (атомарні зміни)
   ```bash
   git add .
   git commit -m "feat(catalog): add Product entity"
   git commit -m "feat(catalog): add IProductRepository interface"
   ```

3. **Push to remote**
   ```bash
   git push -u origin feature/your-feature
   ```

4. **Створіть Pull Request** на GitHub
   - Використовуйте [PR Template](#pull-request-template)
   - Link до related issue
   - Додайте screenshots (якщо UI)

5. **Code Review** (мінімум 1 approval)

6. **Merge** після approval
   - **Squash and merge** (рекомендовано) - для чистої історії
   - **Merge commit** - для великих features
   - **Rebase and merge** - для лінійної історії

7. **Видаліть branch** після merge

---

## Pull Request Process

### PR Template

```markdown
## Description
Brief description of what this PR does.

## Related Issue
Closes #123

## Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update

## Changes Made
- Added product search API endpoint
- Implemented filtering by price and category
- Added unit tests for search logic

## Testing
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Manual testing completed
- [ ] Tested on local environment
- [ ] Tested with Docker

## Screenshots (if applicable)
[Add screenshots here]

## Checklist
- [ ] My code follows the coding conventions
- [ ] I have performed a self-review
- [ ] I have commented my code where necessary
- [ ] I have updated documentation
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix/feature works
- [ ] New and existing unit tests pass
- [ ] Any dependent changes have been merged

## Additional Notes
Any additional information for reviewers.
```

### Code Review Guidelines

#### For Authors

1. **Self-review first** перед створенням PR
2. **Keep PRs small** (<400 lines якщо можливо)
3. **Write clear PR description**
4. **Add tests** для нового коду
5. **Update documentation** якщо потрібно
6. **Respond to comments** швидко
7. **Don't take feedback personally** - це про код, не про вас

#### For Reviewers

1. **Review within 24 hours**
2. **Be constructive and respectful**
3. **Ask questions** замість наказів
   - ❌ "Change this to X"
   - ✅ "What do you think about using X here? It might improve performance."
4. **Approve if minor changes needed**
5. **Test locally** якщо складна логіка
6. **Focus on**:
   - Logic errors
   - Security issues
   - Performance problems
   - Code readability
   - Test coverage

#### Review Checklist

```markdown
- [ ] Code follows conventions
- [ ] Logic is correct
- [ ] No security vulnerabilities
- [ ] Tests are adequate
- [ ] No performance issues
- [ ] Documentation is updated
- [ ] Error handling is proper
- [ ] Logging is appropriate
- [ ] No hardcoded values
- [ ] Dependencies are necessary
```

---

## Definition of Done

Task вважається завершеним коли:

### Code Quality
- [ ] Code написаний згідно conventions
- [ ] No compiler warnings
- [ ] No code smells (SonarQube pass)
- [ ] Code reviewed and approved

### Testing
- [ ] Unit tests written (>80% coverage)
- [ ] Integration tests written (якщо потрібно)
- [ ] All tests pass locally
- [ ] All tests pass on CI

### Documentation
- [ ] XML comments додані для public API
- [ ] README/docs оновлені (якщо потрібно)
- [ ] API documentation оновлена (Swagger)

### Functionality
- [ ] Feature працює як очікувалося
- [ ] Tested manually on local environment
- [ ] Tested with different scenarios (happy path + edge cases)
- [ ] No regression bugs

### CI/CD
- [ ] Build passes on CI
- [ ] No new security alerts
- [ ] No dependency vulnerabilities

### Version Control
- [ ] Commits atomic and well-described
- [ ] Branch merged to main
- [ ] Feature branch deleted

---

## Communication Channels

### Primary Channels

| Channel | Purpose | Response Time |
|---------|---------|---------------|
| **GitHub Issues** | Bug reports, feature requests | 1-2 days |
| **GitHub Discussions** | Questions, ideas, planning | 1-3 days |
| **Pull Request Comments** | Code review | 24 hours |
| **Slack/Discord** | Quick questions, daily standup | 2-4 hours |
| **Email** | Official communications | 1-2 days |

### Issue Labels

- `bug` - Something isn't working
- `feature` - New feature request
- `documentation` - Documentation improvements
- `good first issue` - Good for newcomers
- `help wanted` - Extra attention needed
- `priority: high` - Critical issue
- `priority: medium` - Important but not critical
- `priority: low` - Nice to have
- `wip` - Work in progress
- `blocked` - Blocked by dependencies

---

## Meeting Schedule

### Daily Standup (Async)
- **When**: Every day before 10:00 AM
- **Where**: Slack #standup channel
- **Format**:
  ```
  Yesterday: [What you did]
  Today: [What you plan to do]
  Blockers: [Any blockers]
  ```

### Weekly Sprint Planning
- **When**: Monday 10:00 AM
- **Duration**: 1 hour
- **Agenda**:
  - Review last week progress
  - Plan this week tasks
  - Assign tasks
  - Discuss blockers

### Bi-weekly Retrospective
- **When**: Every 2nd Friday 3:00 PM
- **Duration**: 1 hour
- **Format**:
  - What went well
  - What didn't go well
  - Action items for improvement

### Code Review Sessions (Optional)
- **When**: Wednesday 2:00 PM
- **Duration**: 30 minutes
- **Purpose**: Discuss complex PRs together

---

## Incident Response

### Severity Levels

| Level | Description | Response Time | Example |
|-------|-------------|---------------|---------|
| **P0 - Critical** | Production down | Immediate | Payment service crashed |
| **P1 - High** | Major feature broken | 1 hour | Order creation fails |
| **P2 - Medium** | Minor feature broken | 4 hours | Search returns wrong results |
| **P3 - Low** | Cosmetic issue | 1-2 days | UI alignment issue |

### Incident Response Process

1. **Detect** - Monitoring alerts / User report
2. **Assess** - Determine severity
3. **Notify** - Alert team via Slack #incidents
4. **Investigate** - Check logs (Seq), metrics (Grafana)
5. **Fix** - Apply hotfix or rollback
6. **Verify** - Test in production
7. **Post-mortem** - Write incident report
8. **Improve** - Implement preventive measures

---

## Best Practices

### Performance

```csharp
// ✅ Use async for I/O operations
await _repository.SaveAsync(entity);

// ✅ Use CancellationToken
public async Task<Product> GetProductAsync(int id, CancellationToken ct)
{
    return await _repository.GetByIdAsync(id, ct);
}

// ✅ Use pagination for large datasets
public async Task<PagedResult<Product>> GetProductsAsync(int page, int pageSize)
{
    return await _repository.GetPagedAsync(page, pageSize);
}

// ✅ Cache frequently accessed data
_cache.GetOrCreateAsync("products", async () => await _repository.GetAllAsync());
```

### Security

```csharp
// ✅ Validate input
if (string.IsNullOrWhiteSpace(email))
    throw new ArgumentException("Email is required", nameof(email));

// ✅ Sanitize user input (prevent SQL injection)
// Use parameterized queries (EF Core does this by default)

// ✅ Use HTTPS only in production
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

// ✅ Don't commit secrets
// Use User Secrets (development) or Azure Key Vault (production)
dotnet user-secrets set "JwtSettings:Secret" "your-secret-key"
```

### Error Handling

```csharp
// ✅ Use specific exceptions
throw new ProductNotFoundException(id);

// ✅ Log exceptions with context
_logger.LogError(ex, "Failed to create order. OrderId: {OrderId}", orderId);

// ✅ Return meaningful error messages (but don't expose internals)
return Problem(
    title: "Order creation failed",
    detail: "Unable to process order at this time",
    statusCode: StatusCodes.Status500InternalServerError
);
```

---

## Useful Resources

- [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [React + TypeScript Best Practices](https://react-typescript-cheatsheet.netlify.app/)
- [Conventional Commits](https://www.conventionalcommits.org/)
- [Clean Code by Robert Martin](https://www.amazon.com/Clean-Code-Handbook-Software-Craftsmanship/dp/0132350882)

---

## Agreement

Я погоджуюся дотримуватися цих правил:

- ✅ Я прочитав та зрозумів Team Agreement
- ✅ Я буду дотримуватися coding conventions
- ✅ Я буду робити code review для інших
- ✅ Я буду respectful до команди
- ✅ Я повідомлю про blockers вчасно

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15  

**Ці правила можуть бути оновлені. Всі зміни обговорюються на team meeting.**
