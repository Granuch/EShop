using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Extensions;
using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Application.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Threading.RateLimiting;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// Configure Serilog early to catch startup errors
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Identity Service...");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from appsettings.json
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "EShop.Identity.API"));

    // Add Infrastructure services (DbContext, Identity, Token Service, etc.)
    builder.Services.AddIdentityInfrastructure(builder.Configuration);

    // Add Application services (MediatR, FluentValidation, etc.)
    builder.Services.AddIdentityApplication();

    // Configure JWT Authentication
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

    // Add Authorization
    builder.Services.AddAuthorization();

    // Add Rate Limiting
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Global rate limiter - 100 requests per minute per IP
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1)
                }));

        // Strict rate limiter for auth endpoints - 10 requests per minute
        options.AddFixedWindowLimiter("auth", limiterOptions =>
        {
            limiterOptions.AutoReplenishment = true;
            limiterOptions.PermitLimit = 10;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
        });

        // Login rate limiter - 5 attempts per minute (more strict)
        options.AddFixedWindowLimiter("login", limiterOptions =>
        {
            limiterOptions.AutoReplenishment = true;
            limiterOptions.PermitLimit = 5;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
        });
    });

    // Add CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    // Add Health Checks
    builder.Services.AddHealthChecks()
        .AddNpgSql(builder.Configuration.GetConnectionString("IdentityDb")!);

    // Add Controllers and OpenAPI
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // Add Serilog request logging
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    // Apply database migrations and seed data in Development
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.MigrateAsync();

        // Seed default roles and admin user
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await SeedData.SeedRolesAndAdminAsync(roleManager, userManager, Log.Logger);
    }

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        // OpenAPI JSON endpoint
        app.MapOpenApi();

        // Scalar UI for API documentation
        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("EShop Identity API")
                .WithTheme(ScalarTheme.Purple)
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });
    }

    // Add Rate Limiting middleware
    app.UseRateLimiter();

    // Add CORS
    app.UseCors("AllowFrontend");

    app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");

    Log.Information("Identity Service started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Identity Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
