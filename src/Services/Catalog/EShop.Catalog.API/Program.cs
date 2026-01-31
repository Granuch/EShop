using System.Reflection;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Catalog.API.Endpoints;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.Infrastructure.Repositories;
using EShop.Identity.Infrastructure.Configuration;
using FluentValidation;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
Log.Logger = new LoggerConfiguration()
     .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
     .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
     .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
     .Enrich.FromLogContext()
     .WriteTo.Console()
     .CreateBootstrapLogger();
try
{
     Log.Information("Starting Catalog Service...");
     // Configure Serilog for structured logging
     builder.Host.UseSerilog((context, configuration) => 
          configuration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console());

     // Add DbContext with PostgreSQL
     var connectionString = builder.Configuration.GetConnectionString("CatalogDatabase");

     builder.Services.AddDbContext<CatalogDbContext>(options =>
          options.UseNpgsql(connectionString));

     // TODO: Add Redis distributed cache
     // builder.Services.AddStackExchangeRedisCache(options =>
     // {
     //     options.Configuration = builder.Configuration.GetConnectionString("Redis");
     //     options.InstanceName = "Catalog_";
     // });

     // Add MediatR for CQRS
     builder.Services.AddMediatR(cfg => 
          cfg.RegisterServicesFromAssembly(typeof(GetProductsQuery).Assembly));

     // Add FluentValidation
     builder.Services.AddValidatorsFromAssembly(typeof(CreateProductCommandValidator).Assembly);
     builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
     builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

     // Add repositories
     builder.Services.AddScoped<IProductRepository, ProductRepository>();
     builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
     builder.Services.AddMemoryCache();
     // Add Mapster
     var config = TypeAdapterConfig.GlobalSettings;
     config.Scan(Assembly.GetExecutingAssembly()); // Scans the current assembly
     // If your mappings are in the Application layer, also scan that assembly:
     config.Scan(typeof(ProductDto).Assembly);

     builder.Services.AddSingleton(config);
     builder.Services.AddScoped<IMapper, ServiceMapper>();

     // Add JWT Authentication
     var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
     builder.Services.AddAuthentication(options =>
     {
          options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
          options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
     })
     .AddJwtBearer(options =>
     {
        options.Audience = jwtSettings.Audience;
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
             ValidateIssuer =  true,
             ValidIssuer = jwtSettings.Issuer,
             ValidateAudience = true,
             ValidAudience = jwtSettings.Audience,
             ValidateLifetime =  true
        };
     });
     
     // Add Authorization policies
      builder.Services.AddAuthorization(options =>
      {
          options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
      });

     // Add CORS
     builder.Services.AddCors(options =>
     {
          options.AddPolicy("AllowFrontend", policy =>
               policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
     });

     // Add Health Checks
     builder.Services.AddHealthChecks()
          .AddNpgSql(builder.Configuration.GetConnectionString("CatalogDatabase")!);
     //     .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

     // Add OpenTelemetry for distributed tracing
     var otlEndpoint = builder.Configuration["Jaeger:Endpoint"] ?? "http://localhost:4317";
      builder.Services.AddOpenTelemetry()
          .WithTracing(tracing => tracing
              .AddAspNetCoreInstrumentation()
              .AddHttpClientInstrumentation()
              .AddEntityFrameworkCoreInstrumentation()
              .AddOtlpExporter(options => 
                  options.Endpoint = new Uri(builder.Configuration["Jaeger:Endpoint"]!)));

     // TODO: Add Output Caching for GET requests
     // builder.Services.AddOutputCache(options =>
     // {
     //     options.AddBasePolicy(builder => builder.Expire(TimeSpan.FromMinutes(5)));
     // });

     // Add OpenAPI
     builder.Services.AddEndpointsApiExplorer();
     builder.Services.AddOpenApi();

     var app = builder.Build();

     // TODO: Apply database migrations automatically in Development
     // if (app.Environment.IsDevelopment())
     // {
     //     using var scope = app.Services.CreateScope();
     //     var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
     //     await dbContext.Database.MigrateAsync();
     // }
     if (app.Environment.IsDevelopment())
     {
          // OpenAPI JSON endpoint
          app.MapOpenApi();

          // Scalar UI for API documentation
          app.MapScalarApiReference(options =>
          {
               options
                    .WithTitle("EShop Catalog API")
                    .WithTheme(ScalarTheme.Purple)
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
          });
     }
     

     // Add Serilog request logging
     app.UseSerilogRequestLogging();

     // Add CORS
     app.UseCors("AllowFrontend");

     if (!app.Environment.IsDevelopment())
     {
          app.UseHttpsRedirection();
     }

     // Add Authentication & Authorization
     app.UseAuthentication();
     app.UseAuthorization();

     // Add Output Caching
     // app.UseOutputCache();

     // Map Minimal API endpoints
     app.MapProductEndpoints();
     app.MapCategoryEndpoints();

     // Map Health Checks endpoint
     app.MapHealthChecks("/health");

     app.Run();
}
catch (Exception e)
{
     Console.WriteLine(e);
     throw;
}

