# 🔐 Phase 2: Identity Service Implementation

**Duration**: 2 weeks  
**Team Size**: 2 developers  
**Prerequisites**: Phase 1 completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Implement user authentication (JWT)
- ✅ User registration with email confirmation
- ✅ Password reset functionality
- ✅ Two-factor authentication (2FA)
- ✅ Account lockout protection
- ✅ Role-based access control (RBAC)
- ✅ OAuth2 integration (Google, GitHub)

---

## Architecture

```
EShop.Identity/
├── EShop.Identity.API/          # Controllers, Middleware
├── EShop.Identity.Application/  # Commands, Queries, Validators
├── EShop.Identity.Domain/       # Entities, Value Objects, Events
└── EShop.Identity.Infrastructure/ # DbContext, Repositories, Services
```

---

## Tasks Breakdown

### 2.1 Create Project Structure

**Estimated Time**: 0.5 day

```bash
cd src/Services/Identity

# Create projects
dotnet new webapi -n EShop.Identity.API
dotnet new classlib -n EShop.Identity.Application
dotnet new classlib -n EShop.Identity.Domain
dotnet new classlib -n EShop.Identity.Infrastructure

# Add to solution
dotnet sln add EShop.Identity.API
dotnet sln add EShop.Identity.Application
dotnet sln add EShop.Identity.Domain
dotnet sln add EShop.Identity.Infrastructure

# Add project references
cd EShop.Identity.API
dotnet add reference ../EShop.Identity.Application
dotnet add reference ../EShop.Identity.Infrastructure

cd ../EShop.Identity.Application
dotnet add reference ../EShop.Identity.Domain

cd ../EShop.Identity.Infrastructure
dotnet add reference ../EShop.Identity.Domain
dotnet add reference ../EShop.Identity.Application
```

---

### 2.2 Install NuGet Packages

**Estimated Time**: 0.5 day

```xml
<!-- EShop.Identity.API/EShop.Identity.API.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.0" />
  <PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="9.0.0" />
  <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="9.0.0" />
  <PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
  <PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
  <PackageReference Include="Serilog.Sinks.Seq" Version="6.0.0" />
</ItemGroup>

<!-- EShop.Identity.Application/EShop.Identity.Application.csproj -->
<ItemGroup>
  <PackageReference Include="MediatR" Version="12.2.0" />
  <PackageReference Include="FluentValidation" Version="11.9.0" />
  <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.9.0" />
  <PackageReference Include="AutoMapper" Version="12.0.1" />
</ItemGroup>

<!-- EShop.Identity.Infrastructure/EShop.Identity.Infrastructure.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="9.0.0" />
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="9.0.0" />
  <PackageReference Include="MailKit" Version="4.3.0" />
</ItemGroup>
```

---

### 2.3 Domain Layer Implementation

**Estimated Time**: 1 day

**ApplicationUser Entity:**

```csharp
// EShop.Identity.Domain/Entities/ApplicationUser.cs

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    
    public string? GoogleId { get; set; }
    public string? GitHubId { get; set; }
    
    public string FullName => $"{FirstName} {LastName}".Trim();
}
```

**RefreshToken Value Object:**

```csharp
// EShop.Identity.Domain/ValueObjects/RefreshToken.cs

public record RefreshToken
{
    public string Token { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string? CreatedByIp { get; init; }
    
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsActive => !IsExpired;
}
```

**Domain Events:**

```csharp
// EShop.Identity.Domain/Events/UserRegisteredEvent.cs

public record UserRegisteredEvent(
    string UserId,
    string Email,
    string FirstName,
    string LastName
) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

---

### 2.4 Infrastructure Layer

**Estimated Time**: 2 days

**DbContext:**

```csharp
// EShop.Identity.Infrastructure/Data/IdentityDbContext.cs

public class IdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        
        // Rename Identity tables
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<ApplicationRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("role_claims");
    }
}
```

**TokenService:**

```csharp
// EShop.Identity.Infrastructure/Services/TokenService.cs

public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly IDistributedCache _cache;

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
            new("firstName", user.FirstName),
            new("lastName", user.LastName)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
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
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow
        };

        await _cache.SetAsync(
            $"refresh_token:{token}",
            refreshToken,
            TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays));

        return refreshToken;
    }
}
```

---

### 2.5 Application Layer (CQRS)

**Estimated Time**: 3 days

**Register Command:**

```csharp
// EShop.Identity.Application/Auth/Commands/Register/RegisterCommand.cs

public record RegisterCommand : IRequest<Result<AuthResponse>>
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}

// Validator
public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(100);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$")
            .WithMessage("Password must contain at least 1 uppercase, 1 lowercase, 1 digit, and 1 special character");

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(50);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(50);
    }
}

// Handler
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public async Task<Result<AuthResponse>> Handle(
        RegisterCommand request,
        CancellationToken cancellationToken)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return Result<AuthResponse>.Failure(
                new Error("USER_EXISTS", "User with this email already exists"));
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return Result<AuthResponse>.Failure(
                new Error("REGISTRATION_FAILED", string.Join(", ", result.Errors.Select(e => e.Description))));
        }

        await _userManager.AddToRoleAsync(user, "User");

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailConfirmationAsync(user.Email, token);

        _logger.LogInformation("User {Email} registered successfully", user.Email);

        return Result<AuthResponse>.Success(new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email!,
            Message = "Registration successful. Please check your email to confirm."
        });
    }
}
```

**Login Command:**

```csharp
// EShop.Identity.Application/Auth/Commands/Login/LoginCommand.cs

public record LoginCommand : IRequest<Result<AuthResponse>>
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;

    public async Task<Result<AuthResponse>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return Result<AuthResponse>.Failure(
                new Error("INVALID_CREDENTIALS", "Invalid email or password"));
        }

        if (!user.EmailConfirmed)
        {
            return Result<AuthResponse>.Failure(
                new Error("EMAIL_NOT_CONFIRMED", "Please confirm your email first"));
        }

        var result = await _signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return Result<AuthResponse>.Failure(
                new Error("ACCOUNT_LOCKED", "Account locked due to multiple failed attempts"));
        }

        if (!result.Succeeded)
        {
            return Result<AuthResponse>.Failure(
                new Error("INVALID_CREDENTIALS", "Invalid email or password"));
        }

        var roles = await _userManager.GetRolesAsync(user);
        var tokens = await _tokenService.GenerateTokensAsync(user, roles);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return Result<AuthResponse>.Success(new AuthResponse
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresIn = tokens.ExpiresIn,
            TokenType = "Bearer",
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Roles = roles.ToList()
            }
        });
    }
}
```

---

### 2.6 API Layer (Controllers)

**Estimated Time**: 2 days

**AuthController:**

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

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Registration attempt for {Email}", command.Email);
        
        var result = await _mediator.Send(command, cancellationToken);
        
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new ProblemDetails
            {
                Title = "Registration failed",
                Detail = result.Error!.Message,
                Status = StatusCodes.Status400BadRequest
            });
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(
        [FromBody] LoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        
        if (!result.IsSuccess)
        {
            return result.Error!.Code switch
            {
                "EMAIL_NOT_CONFIRMED" => BadRequest(new { message = result.Error.Message }),
                "ACCOUNT_LOCKED" => StatusCode(StatusCodes.Status423Locked, new { message = result.Error.Message }),
                _ => Unauthorized(new { message = "Invalid credentials" })
            };
        }

        return Ok(result.Value);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Unauthorized();
    }

    [HttpPost("revoke-token")]
    [Authorize]
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

### 2.7 Testing

**Estimated Time**: 2 days

**Unit Tests:**

```csharp
// tests/Identity.Tests/RegisterCommandHandlerTests.cs

public class RegisterCommandHandlerTests
{
    [Fact]
    public async Task Handle_WithValidData_ShouldReturnSuccess()
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
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("test@test.com");
    }

    [Fact]
    public async Task Handle_WithExistingEmail_ShouldReturnFailure()
    {
        // Arrange
        var command = new RegisterCommand
        {
            Email = "existing@test.com",
            Password = "Test123!",
            FirstName = "John",
            LastName = "Doe"
        };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("USER_EXISTS");
    }
}
```

**Integration Tests:**

```csharp
[Fact]
public async Task POST_Register_ShouldReturn200()
{
    var request = new
    {
        Email = "test@test.com",
        Password = "Test123!",
        FirstName = "John",
        LastName = "Doe"
    };

    var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

---

## Success Criteria

- [x] User can register with email/password
- [x] User can login and receive JWT tokens
- [x] Email confirmation flow works
- [x] Password reset functionality
- [x] 2FA can be enabled/verified
- [x] Account lockout after 5 failed attempts
- [x] All tests passing (> 80% coverage)

---

## Next Phase

→ [Phase 3: Catalog Service Implementation](phase-3-catalog.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
