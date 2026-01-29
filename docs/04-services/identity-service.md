# 🔐 Identity Service

Сервіс автентифікації та авторизації користувачів з підтримкою JWT, OAuth2, та 2FA.

---

## Огляд

Identity Service відповідає за:
- ✅ Реєстрацію користувачів
- ✅ Автентифікацію (Login/Logout)
- ✅ JWT Token Management (Access + Refresh)
- ⬜ OAuth2 (Google, GitHub) - TODO
- ✅ Двофакторну автентифікацію (2FA/TOTP)
- ✅ Email Confirmation
- ✅ Password Reset
- ✅ Account Lockout після N невдалих спроб
- ✅ Rate Limiting
- ✅ Управління профілем користувача
- ✅ Ролі та права доступу (RBAC)
- ✅ Structured Logging (Serilog)

---

## Архітектура

### Технології

| Компонент | Технологія | Призначення |
|-----------|------------|-------------|
| **Framework** | ASP.NET Core 10.0 | Web API |
| **Database** | PostgreSQL | User storage |
| **Logging** | Serilog | Structured logging |
| **Identity** | ASP.NET Core Identity | User management |
| **JWT** | System.IdentityModel.Tokens.Jwt | Token generation |
| **2FA** | TOTP (RFC 6238) | Two-factor authentication |
| **Validation** | FluentValidation | Input validation |
| **CQRS** | MediatR | Command/Query separation |
| **API Docs** | Scalar | OpenAPI documentation |

### Clean Architecture Layers

```
EShop.Identity.API/
├── Controllers/
│   ├── AuthController.cs           # Login, Register, Refresh Token
│   ├── AccountController.cs        # Profile, Password, 2FA
│   └── RolesController.cs          # Admin: Manage roles
├── Program.cs                      # Serilog configuration
├── appsettings.json               # Serilog sinks configuration
└── logs/                          # Log files (daily rolling)

EShop.Identity.Domain/
├── Entities/
│   ├── ApplicationUser.cs          # Extends IdentityUser
│   ├── ApplicationRole.cs          # Extends IdentityRole
│   └── RefreshTokenEntity.cs       # DB-stored refresh tokens
├── ValueObjects/
│   ├── RefreshToken.cs
│   └── EmailConfirmationToken.cs
├── Interfaces/
│   ├── ITokenService.cs
│   ├── IRefreshTokenRepository.cs
│   └── IUserRepository.cs
└── Events/
    ├── UserRegisteredEvent.cs
    ├── UserEmailConfirmedEvent.cs
    └── UserPasswordResetRequestedEvent.cs

EShop.Identity.Application/
├── Auth/
│   ├── Commands/
│   │   ├── RegisterCommand.cs
│   │   ├── LoginCommand.cs
│   │   ├── RefreshTokenCommand.cs
│   │   ├── RevokeTokenCommand.cs
│   │   ├── ConfirmEmailCommand.cs
│   │   ├── ForgotPasswordCommand.cs
│   │   └── ResetPasswordCommand.cs
│   └── Queries/
│       └── GetUserByEmailQuery.cs
├── Account/
│   ├── Commands/
│   │   ├── UpdateProfileCommand.cs
│   │   ├── ChangePasswordCommand.cs
│   │   ├── Enable2FACommand.cs
│   │   ├── Verify2FACommand.cs
│   │   └── Disable2FACommand.cs
│   └── Queries/
│       └── GetProfileQuery.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs

EShop.Identity.Infrastructure/
├── Data/
│   ├── IdentityDbContext.cs
│   ├── SeedData.cs
│   └── Migrations/
├── Repositories/
│   ├── UserRepository.cs
│   └── RefreshTokenRepository.cs
├── Services/
│   └── TokenService.cs             # JWT generation & refresh token storage
├── Configuration/
│   └── JwtSettings.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs
```

---

## Logging (Serilog)

Identity Service використовує **Serilog** для structured logging.

### Конфігурація

```json
// appsettings.json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/identity-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ]
  }
}
```

### Request Logging

Кожен HTTP запит автоматично логується:

```
[14:32:15 INF] HTTP POST /api/v1/auth/login responded 200 in 45.2345 ms
```

### Enrichers

Логи автоматично збагачуються:
- `Application` - назва сервісу
- `MachineName` - ім'я серверу
- `ThreadId` - ID потоку
- `EnvironmentName` - Development/Production
- `RequestHost` - хост запиту
- `UserAgent` - браузер/клієнт
- `RemoteIP` - IP адреса клієнта

### Log Files

- Зберігаються в `logs/` директорії
- Rolling: новий файл щодня
- Retention: 7 днів
- Format: `identity-20260129.log`

---

## Domain Entities

### ApplicationUser

```csharp
// EShop.Identity.Domain/Entities/ApplicationUser.cs

public class ApplicationUser : IdentityUser
{
    // Додаткові поля
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }

    // Account Status
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // OAuth
    public string? GoogleId { get; set; }
    public string? GitHubId { get; set; }

    // 2FA
    public bool TwoFactorEnabled { get; set; } = false;
    public string? TwoFactorSecret { get; set; }

    // Computed properties
    public string FullName => $"{FirstName} {LastName}".Trim();
}
```

### RefreshTokenEntity

```csharp
// EShop.Identity.Domain/Entities/RefreshTokenEntity.cs

public class RefreshTokenEntity
{
    public Guid Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
    public string? ReplacedByToken { get; set; }
    public string? RevokeReason { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;
}
```

---

## API Endpoints

### Authentication Endpoints

#### POST /api/v1/auth/register

Реєстрація нового користувача.

**Request:**
```json
{
  "email": "user@example.com",
  "password": "SecureP@ss123",
  "firstName": "John",
  "lastName": "Doe"
}
```

**Response:** `200 OK`
```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "user@example.com",
  "message": "Registration successful. Please check your email to confirm."
}
```

**Validation Rules:**
- Email: Valid format, unique
- Password: Min 8 chars, 1 uppercase, 1 lowercase, 1 digit, 1 special char
- FirstName/LastName: Required, max 50 chars

---

#### POST /api/v1/auth/login

Вхід у систему.

**Request:**
```json
{
  "email": "user@example.com",
  "password": "SecureP@ss123"
}
```

**Response:** `200 OK`
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "abc123...",
  "expiresIn": 3600,
  "tokenType": "Bearer",
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "roles": ["User"]
  }
}
```

**Error Cases:**
- `400 Bad Request` - Email not confirmed
- `401 Unauthorized` - Invalid credentials
- `423 Locked` - Account locked (too many failed attempts)
- `200 OK (requires2FA: true)` - 2FA required

---

#### POST /api/v1/auth/refresh-token

Оновлення Access Token.

**Request:**
```json
{
  "refreshToken": "abc123..."
}
```

**Response:** `200 OK`
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "def456...",
  "expiresIn": 3600
}
```

---

#### POST /api/v1/auth/revoke-token

Відкликання Refresh Token (Logout).

**Request:**
```json
{
  "refreshToken": "abc123..."
}
```

**Response:** `204 No Content`

---

### Account Management Endpoints

#### GET /api/v1/account/profile

Отримати профіль поточного користувача.

**Headers:**
```
Authorization: Bearer {accessToken}
```

**Response:** `200 OK`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "user@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "profilePictureUrl": "https://...",
  "emailConfirmed": true,
  "twoFactorEnabled": false,
  "createdAt": "2024-01-15T10:00:00Z",
  "lastLoginAt": "2024-01-15T15:30:00Z"
}
```

---

#### PUT /api/v1/account/profile

Оновити профіль.

**Request:**
```json
{
  "firstName": "John",
  "lastName": "Smith",
  "profilePictureUrl": "https://..."
}
```

**Response:** `204 No Content`

---

#### POST /api/v1/account/change-password

Зміна пароля.

**Request:**
```json
{
  "currentPassword": "OldP@ss123",
  "newPassword": "NewP@ss123"
}
```

**Response:** `204 No Content`

---

#### POST /api/v1/account/enable-2fa

Увімкнути двофакторну автентифікацію.

**Response:** `200 OK`
```json
{
  "qrCodeUri": "otpauth://totp/EShop:user@example.com?secret=ABC123...",
  "secret": "ABC123...",
  "message": "Scan QR code with authenticator app"
}
```

---

#### POST /api/v1/account/verify-2fa

Підтвердити 2FA код.

**Request:**
```json
{
  "code": "123456"
}
```

**Response:** `200 OK`
```json
{
  "message": "2FA enabled successfully"
}
```

---

## Core Implementation

### AuthController

```csharp
// EShop.Identity.API/Controllers/AuthController.cs

[ApiController]
[Route("api/v{version:apiVersion}/auth")]
[ApiVersion("1.0")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IMediator mediator, ILogger<AuthController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user account
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Registration attempt for {Email}", command.Email);
        
        var result = await _mediator.Send(command, cancellationToken);
        
        return result.Match<IActionResult>(
            success => Ok(success),
            error => BadRequest(new ProblemDetails
            {
                Title = "Registration failed",
                Detail = error.Message,
                Status = StatusCodes.Status400BadRequest
            })
        );
    }

    /// <summary>
    /// Login with email and password
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(
        [FromBody] LoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        
        return result.Match<IActionResult>(
            success => Ok(success),
            error => error.Code switch
            {
                "EMAIL_NOT_CONFIRMED" => BadRequest(new { 
                    code = error.Code, 
                    message = "Please confirm your email first" 
                }),
                "REQUIRES_2FA" => Ok(new { 
                    requires2FA = true, 
                    userId = error.Data["userId"] 
                }),
                _ => Unauthorized(new { message = "Invalid credentials" })
            }
        );
    }

    /// <summary>
    /// Refresh access token
    /// </summary>
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Unauthorized();
    }

    /// <summary>
    /// Revoke refresh token (logout)
    /// </summary>
    [HttpPost("revoke-token")]
    public async Task<IActionResult> RevokeToken(
        [FromBody] RevokeTokenCommand command,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
```

---

### TokenService (JWT Generation)

```csharp
// EShop.Identity.Infrastructure/Services/TokenService.cs

public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly IDistributedCache _cache;
    private readonly TimeProvider _timeProvider;

    public TokenService(
        IOptions<JwtSettings> jwtSettings,
        IDistributedCache cache,
        TimeProvider timeProvider)
    {
        _jwtSettings = jwtSettings.Value;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public async Task<TokenResult> GenerateTokensAsync(
        ApplicationUser user, 
        IList<string> roles)
    {
        var accessToken = GenerateAccessToken(user, roles);
        var refreshToken = await GenerateRefreshTokenAsync(user.Id);

        return new TokenResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = _jwtSettings.AccessTokenExpirationMinutes * 60,
            TokenType = "Bearer"
        };
    }

    private string GenerateAccessToken(ApplicationUser user, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, 
                _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(), 
                ClaimValueTypes.Integer64),
            new("firstName", user.FirstName ?? ""),
            new("lastName", user.LastName ?? "")
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: _timeProvider.GetUtcNow().UtcDateTime,
            expires: _timeProvider.GetUtcNow()
                .AddMinutes(_jwtSettings.AccessTokenExpirationMinutes).UtcDateTime,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<RefreshToken> GenerateRefreshTokenAsync(string userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshToken = new RefreshToken
        {
            Token = token,
            UserId = userId,
            ExpiresAt = _timeProvider.GetUtcNow()
                .AddDays(_jwtSettings.RefreshTokenExpirationDays).UtcDateTime,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };

        // Store in Redis with expiration
        await _cache.SetStringAsync(
            $"refresh_token:{token}",
            JsonSerializer.Serialize(refreshToken),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = 
                    TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays)
            });

        return refreshToken;
    }

    public async Task<bool> RevokeRefreshTokenAsync(string token)
    {
        await _cache.RemoveAsync($"refresh_token:{token}");
        return true;
    }

    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string token)
    {
        var cachedToken = await _cache.GetStringAsync($"refresh_token:{token}");
        
        if (string.IsNullOrEmpty(cachedToken))
            return null;
            
        var refreshToken = JsonSerializer.Deserialize<RefreshToken>(cachedToken);
        
        return refreshToken?.IsActive == true ? refreshToken : null;
    }
}
```

---

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=identity;Username=eshop;Password=eshop123"
  },
  
  "JwtSettings": {
    "SecretKey": "ThisIsAVerySecureSecretKeyForJWT12345",
    "Issuer": "EShop.Identity",
    "Audience": "EShop.Clients",
    "AccessTokenExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 30
  },
  
  "EmailSettings": {
    "SmtpHost": "smtp.mailtrap.io",
    "SmtpPort": 587,
    "SmtpUser": "your-username",
    "SmtpPassword": "your-password",
    "SenderEmail": "noreply@eshop.com",
    "SenderName": "E-Shop"
  },
  
  "RateLimiting": {
    "Auth": {
      "PermitLimit": 5,
      "Window": 60
    }
  },
  
  "AccountLockout": {
    "MaxFailedAttempts": 5,
    "LockoutDurationMinutes": 15
  }
}
```

### Program.cs Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    
    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    
    // User settings
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<IdentityDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.Run();
```

---

## Security Best Practices

### ✅ Implemented

1. **Password Hashing** - ASP.NET Core Identity uses PBKDF2 with SHA-256
2. **JWT Signing** - HMAC-SHA256 algorithm
3. **Refresh Token Rotation** - New refresh token on each use
4. **Rate Limiting** - 5 login attempts per minute
5. **Account Lockout** - After 5 failed attempts, 15 min lockout
6. **HTTPS Only** - In production
7. **Token Expiration** - Access token: 1 hour, Refresh: 30 days
8. **Secure Token Storage** - Refresh tokens in Redis with TTL

### ❌ NOT Implemented (Future)

- Token Blacklisting (on logout)
- IP-based Geolocation blocking
- Email/SMS for suspicious activity
- Multi-device session management
- CAPTCHA after N failed attempts

---

## Testing

### Unit Tests

```csharp
[Fact]
public async Task Register_WithValidData_ShouldReturnSuccess()
{
    // Arrange
    var command = new RegisterCommand
    {
        Email = "test@test.com",
        Password = "Test123!",
        FirstName = "John",
        LastName = "Doe"
    };
    
    // Act
    var result = await _mediator.Send(command);
    
    // Assert
    result.IsSuccess.Should().BeTrue();
    result.Value.Email.Should().Be("test@test.com");
}
```

### Integration Tests

```csharp
[Fact]
public async Task POST_Register_ShouldReturn200()
{
    // Arrange
    var request = new
    {
        Email = "test@test.com",
        Password = "Test123!",
        FirstName = "John",
        LastName = "Doe"
    };
    
    // Act
    var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
    
    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

---

## Наступні кроки

- ✅ [Catalog Service](catalog-service.md) - Product management
- ✅ [API Gateway](api-gateway.md) - JWT validation
- ✅ [Security Hardening](../../10-production-readiness/security-hardening.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
