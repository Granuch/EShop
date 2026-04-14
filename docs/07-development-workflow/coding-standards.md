# 👀 Code Review Process

Процес code review для забезпечення якості коду.

---

## Code Review Guidelines

### Goals

- ✅ **Quality**: Catch bugs, improve design
- ✅ **Knowledge Sharing**: Spread domain knowledge across team
- ✅ **Consistency**: Maintain coding standards
- ✅ **Learning**: Mentor junior developers

---

## Review Process

### 1. Before Submitting PR

**Author checklist**:

- [ ] Code compiles without errors
- [ ] All tests pass locally
- [ ] Self-review completed (read your own code)
- [ ] No commented-out code
- [ ] No debug statements (`Console.WriteLine`, `console.log`)
- [ ] XML comments for public APIs
- [ ] Tests added for new features
- [ ] Documentation updated (README, API docs)

---

### 2. Creating Pull Request

**Good PR**:
- ✅ Small size (< 400 lines changed)
- ✅ Single responsibility (one feature/bug)
- ✅ Clear title and description
- ✅ Screenshots for UI changes
- ✅ Links to related issues

**PR Description Template**:

```markdown
## What
Brief description of what this PR does.

## Why
Why is this change needed?

## How
How does this PR solve the problem?

## Testing
- [ ] Unit tests added
- [ ] Integration tests added
- [ ] Manual testing steps:
  1. Login as admin
  2. Navigate to Products page
  3. Click "Add Product"
  4. Verify validation works

## Screenshots
[Add before/after screenshots for UI changes]

## Breaking Changes
None / List any breaking changes

## Checklist
- [ ] Tests passing
- [ ] No merge conflicts
- [ ] Documentation updated
```

---

### 3. Reviewer Guidelines

**Who reviews**:
- Minimum **2 reviewers** required
- At least 1 senior developer
- Code owner (if file has CODEOWNERS)

**Response Time**:
- **Critical fixes**: Within 2 hours
- **Features**: Within 1 business day
- **Documentation**: Within 2 business days

---

## What to Review

### 1. Functionality

**Questions to ask**:
- ❓ Does the code do what it's supposed to?
- ❓ Are there edge cases not handled?
- ❓ Is error handling sufficient?
- ❓ Are there potential race conditions?

**Example**:

```csharp
// ❌ Missing null check
public async Task<Product> GetProductByIdAsync(Guid id)
{
    var product = await _context.Products.FindAsync(id);
    return product; // What if product is null?
}

// ✅ Better
public async Task<Product?> GetProductByIdAsync(Guid id)
{
    return await _context.Products.FindAsync(id);
    // Or throw NotFoundException if null not allowed
}
```

---

### 2. Design & Architecture

**Questions**:
- ❓ Does it follow Clean Architecture?
- ❓ Is business logic in Domain layer?
- ❓ Are dependencies pointing inward?
- ❓ Is SOLID followed?

**Example**:

```csharp
// ❌ Domain logic in Controller
[HttpPost]
public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
{
    // Validation logic (should be in validator)
    if (request.Items.Count == 0)
        return BadRequest("Order must have items");
    
    // Business logic (should be in domain)
    var total = request.Items.Sum(i => i.Price * i.Quantity);
    
    // Data access (should be in repository)
    var order = new Order { Total = total };
    _context.Orders.Add(order);
    await _context.SaveChangesAsync();
    
    return Ok(order.Id);
}

// ✅ Better: Use CQRS + MediatR
[HttpPost]
public async Task<IActionResult> CreateOrder(CreateOrderCommand command)
{
    var result = await _mediator.Send(command);
    return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
}
```

---

### 3. Code Quality

**Check for**:
- ✅ Readable variable names (`productCount` vs `cnt`)
- ✅ Short methods (< 20 lines)
- ✅ Single Responsibility Principle
- ✅ DRY (Don't Repeat Yourself)
- ✅ No magic numbers (use constants)

**Example**:

```csharp
// ❌ Magic numbers
if (user.Age >= 18 && order.Total > 100)
{
    discount = order.Total * 0.1;
}

// ✅ Named constants
private const int LEGAL_AGE = 18;
private const decimal MINIMUM_ORDER_FOR_DISCOUNT = 100m;
private const decimal DISCOUNT_PERCENTAGE = 0.1m;

if (user.Age >= LEGAL_AGE && order.Total > MINIMUM_ORDER_FOR_DISCOUNT)
{
    discount = order.Total * DISCOUNT_PERCENTAGE;
}
```

---

### 4. Testing

**Check**:
- ✅ Unit tests for new features
- ✅ Tests cover edge cases
- ✅ Test names are descriptive
- ✅ No flaky tests (random failures)

**Example**:

```csharp
// ❌ Poor test name
[Fact]
public void Test1()
{
    // What does this test?
}

// ✅ Better
[Fact]
public void CreateProduct_WithNegativePrice_ShouldThrowDomainException()
{
    // Arrange
    var command = new CreateProductCommand { Price = -10 };
    
    // Act & Assert
    var act = () => Product.Create(command.Name, new Money(command.Price), ...);
    act.Should().Throw<DomainException>()
        .WithMessage("Price must be positive");
}
```

---

### 5. Security

**Check for**:
- ❌ Hardcoded secrets (passwords, API keys)
- ❌ SQL injection vulnerabilities
- ❌ Missing authorization checks
- ❌ Sensitive data in logs

**Example**:

```csharp
// ❌ Hardcoded secret
var connectionString = "Server=prod-db;Password=SuperSecret123;";

// ✅ From configuration
var connectionString = _configuration.GetConnectionString("DefaultConnection");

// ❌ Missing authorization
[HttpDelete("api/v1/users/{id}")]
public async Task<IActionResult> DeleteUser(string id)
{
    // Anyone can delete any user!
}

// ✅ With authorization
[HttpDelete("api/v1/users/{id}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> DeleteUser(string id)
{
    // Only admins can delete users
}
```

---

### 6. Performance

**Check for**:
- ❌ N+1 query problem
- ❌ Missing database indexes
- ❌ Loading unnecessary data
- ❌ Synchronous I/O blocking threads

**Example**:

```csharp
// ❌ N+1 query problem
var products = await _context.Products.ToListAsync();
foreach (var product in products)
{
    var category = await _context.Categories.FindAsync(product.CategoryId);
    // Database query for each product!
}

// ✅ Eager loading
var products = await _context.Products
    .Include(p => p.Category)
    .ToListAsync();

// ❌ Loading all columns when only need few
var products = await _context.Products.ToListAsync();

// ✅ Projection (only needed fields)
var products = await _context.Products
    .Select(p => new ProductDto 
    { 
        Id = p.Id, 
        Name = p.Name, 
        Price = p.Price.Amount 
    })
    .ToListAsync();
```

---

## Review Comments

### How to Comment

**Be specific**:

```markdown
❌ "This is bad"
✅ "This method has cyclomatic complexity of 15. Consider extracting the validation logic into a separate method to improve readability."

❌ "Change this"
✅ "Consider using FirstOrDefaultAsync() instead of First() to avoid throwing exception when no results found. This will make the code more defensive."

❌ "Not performant"
✅ "This N+1 query will cause performance issues. Use Include() to eager-load the Category. See: https://learn.microsoft.com/ef-core/querying/related-data/eager"
```

**Ask questions**:

```markdown
❓ "Why did you choose List<T> over IEnumerable<T> here? IEnumerable might be more appropriate since we're only iterating."

❓ "Could this cause a race condition if two users checkout at the same time?"

❓ "Have you considered what happens if the external API times out here?"
```

**Suggest alternatives**:

```markdown
💡 Suggestion: You might want to use a switch expression here for better readability:

return status switch
{
    OrderStatus.Pending => "Order is being processed",
    OrderStatus.Paid => "Order paid successfully",
    OrderStatus.Shipped => "Order shipped",
    _ => "Unknown status"
};
```

**Praise good code**:

```markdown
🎉 Great use of the Repository pattern! This makes testing much easier.
🎉 Nice error handling! I like how you used Result<T> to avoid exceptions for expected failures.
🎉 Excellent test coverage! The edge cases are well covered.
```

---

### Comment Categories

Use GitHub review features:

- **💬 Comment**: Suggestion or question (optional to address)
- **💡 Suggestion**: Code change proposal (GitHub can apply directly)
- **🚫 Request Changes**: Must be addressed before merge
- **✅ Approve**: LGTM (Looks Good To Me)

---

## Review Checklist

**Automated Checks** (CI/CD):
- [ ] Build succeeds
- [ ] All tests pass
- [ ] Code coverage > 80%
- [ ] No security vulnerabilities (Dependabot)
- [ ] Code quality checks pass (SonarQube)

**Manual Review**:

**Functionality**:
- [ ] Code solves the stated problem
- [ ] Edge cases handled
- [ ] Error handling appropriate
- [ ] No obvious bugs

**Design**:
- [ ] Follows Clean Architecture
- [ ] SOLID principles followed
- [ ] No unnecessary abstractions
- [ ] Appropriate design patterns used

**Code Quality**:
- [ ] Readable and maintainable
- [ ] No code duplication
- [ ] Meaningful variable names
- [ ] Methods < 20 lines
- [ ] Classes < 300 lines

**Testing**:
- [ ] Unit tests for new features
- [ ] Tests cover edge cases
- [ ] Test names descriptive
- [ ] No test duplication

**Security**:
- [ ] No hardcoded secrets
- [ ] Input validation present
- [ ] Authorization checks correct
- [ ] No sensitive data in logs

**Performance**:
- [ ] No N+1 queries
- [ ] Database indexes if needed
- [ ] Async/await used correctly
- [ ] No memory leaks

**Documentation**:
- [ ] XML comments for public APIs
- [ ] README updated if needed
- [ ] Complex logic explained

---

## Common Code Smells

### 1. Long Method

```csharp
// ❌ 50+ lines method
public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
{
    // Validation (10 lines)
    // Business logic (20 lines)
    // Data access (10 lines)
    // Notification (10 lines)
}

// ✅ Extracted
public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
{
    ValidateRequest(request);
    var order = await CreateOrderEntity(request);
    await SendOrderNotification(order);
    return order;
}
```

### 2. God Class

```csharp
// ❌ OrderService does everything
public class OrderService
{
    public async Task CreateOrder() { }
    public async Task CancelOrder() { }
    public async Task ProcessPayment() { }
    public async Task SendEmail() { }
    public async Task UpdateInventory() { }
}

// ✅ Separate responsibilities
public class OrderCommandHandler { } // Create/Cancel orders
public class PaymentService { } // Process payments
public class EmailService { } // Send emails
public class InventoryService { } // Update inventory
```

### 3. Feature Envy

```csharp
// ❌ Method uses more data from another class
public decimal CalculateTotal(Order order)
{
    return order.Items.Sum(i => i.Price * i.Quantity) - order.Discount + order.ShippingCost;
}

// ✅ Move to Order class
public class Order
{
    public decimal CalculateTotal()
    {
        return Items.Sum(i => i.Price * i.Quantity) - Discount + ShippingCost;
    }
}
```

---

## After Review

### Addressing Feedback

**Author**:
1. Address all "Request Changes" comments
2. Reply to questions
3. Apply suggestions or explain why not
4. Re-request review after changes

**Example Response**:

```markdown
> Why did you use List<T> instead of IEnumerable<T>?

Good question! I need List<T> here because I call `.Count()` later which would enumerate IEnumerable twice. I'll add a comment explaining this.

> This could cause a race condition

You're right! I've added a database transaction to prevent this. Updated commit: abc123

> Consider extracting this validation

Done! Created ValidateOrderCommand class. Commit: def456
```

---

## Merge Criteria

**Can merge when**:
- ✅ 2+ approvals
- ✅ All CI checks pass
- ✅ All conversations resolved
- ✅ No merge conflicts
- ✅ Branch up-to-date with base

---

## Review Time Estimates

| PR Size | Review Time | Example |
|---------|-------------|---------|
| **XS** (< 50 lines) | 10 min | Bug fix, typo |
| **S** (50-200 lines) | 30 min | Small feature |
| **M** (200-400 lines) | 1 hour | Medium feature |
| **L** (400-800 lines) | 2 hours | Large feature |
| **XL** (> 800 lines) | 3+ hours | Consider splitting |

**Tip**: Keep PRs small for faster reviews!

---

## Tools

### 1. GitHub Review Features

- **Comment**: Ask questions, suggest improvements
- **Approve**: LGTM
- **Request Changes**: Must fix before merge
- **Suggestion**: Propose code changes (can apply directly)

### 2. Code Review Plugins

- **SonarLint** (IDE extension): Real-time code quality feedback
- **GitHub Copilot**: AI-powered code suggestions
- **ReSharper**: C# code analysis

---

## Team Agreements

- ✅ Respond to reviews within 1 business day
- ✅ Be respectful and constructive
- ✅ Focus on code, not person
- ✅ Explain "why", not just "what"
- ✅ Ask questions instead of demanding changes
- ✅ Praise good code!

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
