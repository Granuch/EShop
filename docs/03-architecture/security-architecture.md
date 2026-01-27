# 🔒 Security Architecture

Документація про security на рівні архітектури системи.

---

## Security Principles

### 1. Defense in Depth (Багатошаровий захист)

Кілька рівнів захисту, щоб атака на один рівень не компрометувала всю систему.

**Layers**:

```
┌────────────────────────────────────────────────────┐
│  Layer 1: Network Security                        │
│  - Firewall                                        │
│  - DDoS protection (Azure DDoS, Cloudflare)        │
│  - WAF (Web Application Firewall)                  │
└────────────────────────────────────────────────────┘
                    ↓
┌────────────────────────────────────────────────────┐
│  Layer 2: API Gateway                              │
│  - Rate limiting (100 req/min per IP)              │
│  - IP whitelisting/blacklisting                    │
│  - Request size limits (10MB max)                  │
└────────────────────────────────────────────────────┘
                    ↓
┌────────────────────────────────────────────────────┐
│  Layer 3: Authentication & Authorization           │
│  - JWT validation                                  │
│  - Role-based access control (RBAC)                │
│  - OAuth2 (Google, GitHub)                         │
└────────────────────────────────────────────────────┘
                    ↓
┌────────────────────────────────────────────────────┐
│  Layer 4: Application Security                     │
│  - Input validation (FluentValidation)             │
│  - SQL injection prevention (EF Core parameterized)│
│  - XSS protection (React escapes by default)       │
│  - CSRF tokens                                     │
└────────────────────────────────────────────────────┘
                    ↓
┌────────────────────────────────────────────────────┐
│  Layer 5: Data Security                            │
│  - Encryption at rest (Azure Disk Encryption)      │
│  - Encryption in transit (TLS 1.3)                 │
│  - Database encryption (PostgreSQL SSL)            │
│  - Secrets management (Azure Key Vault)            │
└────────────────────────────────────────────────────┘
```

---

## Authentication & Authorization

### JWT Token Flow

```
┌──────────┐                                    ┌──────────┐
│ Client   │                                    │ Identity │
│ (React)  │                                    │ Service  │
└────┬─────┘                                    └────┬─────┘
     │                                                │
     │ 1. POST /api/v1/auth/login                    │
     │    { email, password }                         │
     ├───────────────────────────────────────────────►│
     │                                                │
     │                                      2. Validate credentials
     │                                         (check PostgreSQL)
     │                                                │
     │                                      3. Generate tokens:
     │                                         - Access Token (JWT, 15 min)
     │                                         - Refresh Token (random, 7 days)
     │                                                │
     │ 4. Response:                                   │
     │    { accessToken, refreshToken, user }         │
     │◄───────────────────────────────────────────────┤
     │                                                │
     │ 5. Store tokens:                               │
     │    - Access token: memory (not localStorage!)  │
     │    - Refresh token: httpOnly cookie            │
     │                                                │
     │                                                │
     │ 6. GET /api/v1/products                        │
     │    Authorization: Bearer {accessToken}         │
     ├───────────────────────────────────────────────►│
     │                                                │
     │                                      7. Validate JWT:
     │                                         - Signature valid?
     │                                         - Not expired?
     │                                         - Claims correct?
     │                                                │
     │ 8. Response: { products: [...] }               │
     │◄───────────────────────────────────────────────┤
     │                                                │
```

### Access Token (JWT) Structure

```json
{
  "header": {
    "alg": "HS256",
    "typ": "JWT"
  },
  "payload": {
    "sub": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "roles": ["User"],
    "iat": 1705334400,
    "exp": 1705335300,
    "iss": "EShop.Identity",
    "aud": "EShop.Clients"
  },
  "signature": "HMACSHA256(base64UrlEncode(header) + '.' + base64UrlEncode(payload), secret)"
}
```

**Claims**:
- `sub` (Subject): User ID
- `email`: User email
- `roles`: User roles (for authorization)
- `iat` (Issued At): Token creation time
- `exp` (Expiration): Token expiry time
- `iss` (Issuer): Who issued the token
- `aud` (Audience): Who can use the token

**Security**:
- ✅ Signed with HMAC-SHA256 (prevents tampering)
- ✅ Short lifetime (15 minutes)
- ✅ Contains minimal data (no sensitive info)
- ❌ Cannot revoke (must wait for expiry)

---

### Refresh Token Flow

```
┌──────────┐                                    ┌──────────┐
│ Client   │                                    │ Identity │
│          │                                    │ Service  │
└────┬─────┘                                    └────┬─────┘
     │                                                │
     │ Access token expired (401 Unauthorized)        │
     │                                                │
     │ 1. POST /api/v1/auth/refresh-token             │
     │    { refreshToken }                            │
     ├───────────────────────────────────────────────►│
     │                                                │
     │                                      2. Validate refresh token:
     │                                         - Exists in Redis?
     │                                         - Not expired?
     │                                         - Not revoked?
     │                                                │
     │                                      3. Generate new tokens:
     │                                         - New access token
     │                                         - New refresh token (rotation)
     │                                                │
     │                                      4. Revoke old refresh token
     │                                                │
     │ 5. Response:                                   │
     │    { accessToken, refreshToken }               │
     │◄───────────────────────────────────────────────┤
     │                                                │
```

**Refresh Token Rotation**:
- Old refresh token invalidated after use
- Prevents replay attacks

---

### Role-Based Access Control (RBAC)

```csharp
// Authorization policy
[Authorize(Roles = "Admin")]
[HttpPost("api/v1/products")]
public async Task<IActionResult> CreateProduct([FromBody] CreateProductCommand command)
{
    // Only users with "Admin" role can access
}

// Custom policy
[Authorize(Policy = "CanManageOrders")]
[HttpDelete("api/v1/orders/{id}")]
public async Task<IActionResult> CancelOrder(Guid id)
{
    // Custom policy can check multiple conditions
}
```

**Policy Definition**:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanManageOrders", policy =>
    {
        policy.RequireRole("Admin", "OrderManager");
        policy.RequireClaim("permission", "orders.manage");
    });
});
```

**Roles**:
- **User**: Can browse products, place orders
- **Admin**: Full access (manage products, users, orders)
- **OrderManager**: Can manage orders only
- **Support**: Can view orders, cannot modify

---

## Input Validation & Sanitization

### 1. FluentValidation (Server-side)

```csharp
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required")
            .MaximumLength(200).WithMessage("Product name too long")
            .Matches(@"^[a-zA-Z0-9\s\-]+$").WithMessage("Product name contains invalid characters");
        
        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be positive")
            .LessThan(1000000).WithMessage("Price too high");
        
        RuleFor(x => x.Sku)
            .NotEmpty()
            .Length(5, 20)
            .Must(BeUniqueSkum).WithMessage("SKU already exists");
    }
}
```

**Validation Pipeline**:

```csharp
// Automatic validation via MediatR behavior
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        
        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(result => result.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Any())
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
```

---

### 2. SQL Injection Prevention

**❌ Vulnerable (string concatenation)**:

```csharp
// NEVER DO THIS!
var query = $"SELECT * FROM products WHERE name = '{productName}'";
await _context.Database.ExecuteSqlRawAsync(query);
```

**✅ Safe (parameterized queries)**:

```csharp
// EF Core automatically parameterizes
var products = await _context.Products
    .Where(p => p.Name == productName)
    .ToListAsync();

// Or with raw SQL (still parameterized)
await _context.Products
    .FromSqlRaw("SELECT * FROM products WHERE name = {0}", productName)
    .ToListAsync();
```

---

### 3. XSS Protection (React)

**React escapes output by default**:

```tsx
// ✅ Safe - React escapes HTML
<div>{product.description}</div>

// ❌ Dangerous - bypasses React escaping
<div dangerouslySetInnerHTML={{ __html: product.description }} />
```

**CSP (Content Security Policy) Header**:

```csharp
// Program.cs
app.Use(async (context, next) =>
{
    context.Response.Headers.Add(
        "Content-Security-Policy",
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'");
    await next();
});
```

---

### 4. CSRF Protection

**SameSite Cookie**:

```csharp
builder.Services.AddAuthentication()
    .AddCookie(options =>
    {
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // HTTPS only
    });
```

**Anti-Forgery Token**:

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command)
{
    // CSRF token validated automatically
}
```

---

## Secrets Management

### Azure Key Vault Integration

```csharp
// Program.cs
var keyVaultUrl = builder.Configuration["KeyVaultUrl"];

builder.Configuration.AddAzureKeyVault(
    new Uri(keyVaultUrl),
    new DefaultAzureCredential());

// Secrets loaded from Key Vault
var jwtSecret = builder.Configuration["JwtSettings:SecretKey"]; // from Key Vault
var stripeKey = builder.Configuration["Stripe:SecretKey"]; // from Key Vault
```

**Key Vault Secrets**:
- `JwtSettings--SecretKey`: JWT signing key
- `Stripe--SecretKey`: Stripe API key
- `PostgreSQL--Password`: Database password
- `Redis--Password`: Redis password

**❌ Never commit secrets to Git**:

```gitignore
# .gitignore
appsettings.Development.json
appsettings.Production.json
*.secrets.json
.env
```

---

## Encryption

### 1. Encryption in Transit (TLS/HTTPS)

```yaml
# Kubernetes Ingress with TLS
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: eshop-ingress
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod
spec:
  tls:
  - hosts:
    - api.eshop.com
    secretName: eshop-tls
  rules:
  - host: api.eshop.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: api-gateway
            port:
              number: 80
```

**Enforced HTTPS**:

```csharp
// Program.cs
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts(); // HTTP Strict Transport Security
}
```

---

### 2. Encryption at Rest (Database)

**PostgreSQL SSL Connection**:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres.azure.com;Port=5432;Database=catalog;Username=eshop;Password=***;SSL Mode=Require;Trust Server Certificate=false"
  }
}
```

**Azure Disk Encryption** (for Kubernetes persistent volumes):

```hcl
resource "azurerm_kubernetes_cluster" "eshop" {
  # ...
  
  disk_encryption_set_id = azurerm_disk_encryption_set.eshop.id
}
```

---

### 3. Password Hashing

**✅ ASP.NET Core Identity uses PBKDF2**:

```csharp
// Automatic password hashing
var user = new ApplicationUser { UserName = "user@example.com" };
var result = await _userManager.CreateAsync(user, "SecureP@ss123");

// Password is hashed before storing:
// Hash: $MYHASH$V3$10000$... (PBKDF2 with 10,000 iterations)
```

**Never**:
- ❌ Store passwords in plain text
- ❌ Use MD5 or SHA-1 (broken)
- ❌ Use SHA-256 without salt (rainbow table attacks)

---

## Rate Limiting

### API Gateway Level

```csharp
// Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5; // 5 requests
        opt.Window = TimeSpan.FromMinutes(1); // per minute
        opt.QueueLimit = 0; // no queue
    });
    
    options.AddSlidingWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 4; // 4 segments of 15s each
    });
});

// Apply to endpoints
[EnableRateLimiting("auth")]
[HttpPost("api/v1/auth/login")]
public async Task<IActionResult> Login([FromBody] LoginCommand command)
{
    // Max 5 login attempts per minute
}
```

**Response** (rate limit exceeded):

```
HTTP/1.1 429 Too Many Requests
Retry-After: 30
```

---

### User-Level Rate Limiting (Redis)

```csharp
public class UserRateLimitMiddleware
{
    private readonly IDistributedCache _cache;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var userId = context.User.FindFirst("sub")?.Value;
        if (userId == null)
        {
            await next(context);
            return;
        }

        var key = $"rate_limit:{userId}";
        var count = await _cache.GetAsync<int>(key);

        if (count >= 100) // 100 requests per minute
        {
            context.Response.StatusCode = 429;
            await context.Response.WriteAsync("Rate limit exceeded");
            return;
        }

        await _cache.SetAsync(key, count + 1, TimeSpan.FromMinutes(1));
        await next(context);
    }
}
```

---

## Security Headers

```csharp
// Program.cs
app.Use(async (context, next) =>
{
    // Prevent clickjacking
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    
    // XSS protection
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    
    // Referrer policy
    context.Response.Headers.Add("Referrer-Policy", "no-referrer");
    
    // Permissions policy
    context.Response.Headers.Add("Permissions-Policy", "geolocation=(), microphone=()");
    
    await next();
});
```

---

## Security Monitoring & Auditing

### 1. Audit Logs

```csharp
public abstract class Entity
{
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

// Automatically set by DbContext
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var userId = _httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value ?? "system";
    
    foreach (var entry in ChangeTracker.Entries<Entity>())
    {
        if (entry.State == EntityState.Added)
        {
            entry.Entity.CreatedBy = userId;
            entry.Entity.CreatedAt = DateTime.UtcNow;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Entity.ModifiedBy = userId;
            entry.Entity.ModifiedAt = DateTime.UtcNow;
        }
    }
    
    return await base.SaveChangesAsync(ct);
}
```

---

### 2. Security Events Logging

```csharp
// Failed login attempt
_logger.LogWarning(
    "Failed login attempt for user {Email} from IP {IpAddress}",
    loginCommand.Email,
    httpContext.Connection.RemoteIpAddress);

// Successful login
_logger.LogInformation(
    "User {UserId} logged in from IP {IpAddress}",
    user.Id,
    httpContext.Connection.RemoteIpAddress);

// Account lockout
_logger.LogWarning(
    "Account {Email} locked due to {FailedAttempts} failed login attempts",
    user.Email,
    failedAttempts);
```

---

## OWASP Top 10 Protection

| Vulnerability | Mitigation |
|---------------|------------|
| **A01: Broken Access Control** | JWT + RBAC, Authorization policies |
| **A02: Cryptographic Failures** | TLS 1.3, PBKDF2 password hashing, Azure Disk Encryption |
| **A03: Injection** | EF Core parameterized queries, FluentValidation |
| **A04: Insecure Design** | ADRs, Security reviews, Threat modeling |
| **A05: Security Misconfiguration** | Security headers, CSP, Azure Key Vault |
| **A06: Vulnerable Components** | Dependabot, `dotnet list package --vulnerable` |
| **A07: Authentication Failures** | JWT, Refresh token rotation, Account lockout |
| **A08: Data Integrity Failures** | HMAC signatures, TLS, Azure Key Vault |
| **A09: Logging Failures** | Seq, Audit logs, Security event logging |
| **A10: SSRF** | HTTP client whitelist, Validate redirect URLs |

---

## Security Checklist

### Production Deployment

- [ ] All secrets in Azure Key Vault (not appsettings.json)
- [ ] HTTPS enforced (HSTS enabled)
- [ ] Database connections use SSL
- [ ] Rate limiting enabled
- [ ] Security headers configured
- [ ] JWT secrets rotated
- [ ] Account lockout enabled (5 failed attempts)
- [ ] CORS restricted to specific origins
- [ ] File upload size limits (10MB max)
- [ ] Input validation on all endpoints
- [ ] Audit logging enabled
- [ ] Dependency vulnerability scan passed
- [ ] Penetration testing completed
- [ ] Security review by team

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
