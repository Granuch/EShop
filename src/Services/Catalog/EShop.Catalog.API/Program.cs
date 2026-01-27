using EShop.Catalog.API.Endpoints;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// TODO: Configure Serilog for structured logging
// builder.Host.UseSerilog((context, configuration) => 
//     configuration.ReadFrom.Configuration(context.Configuration));

// TODO: Add DbContext with PostgreSQL
// builder.Services.AddDbContext<CatalogDbContext>(options =>
//     options.UseNpgsql(builder.Configuration.GetConnectionString("CatalogDb")));

// TODO: Add Redis distributed cache
// builder.Services.AddStackExchangeRedisCache(options =>
// {
//     options.Configuration = builder.Configuration.GetConnectionString("Redis");
//     options.InstanceName = "Catalog_";
// });

// TODO: Add MediatR for CQRS
// builder.Services.AddMediatR(cfg => 
//     cfg.RegisterServicesFromAssembly(typeof(GetProductsQuery).Assembly));

// TODO: Add FluentValidation
// builder.Services.AddValidatorsFromAssembly(typeof(CreateProductCommandValidator).Assembly);
// builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
// builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// TODO: Add repositories
// builder.Services.AddScoped<IProductRepository, ProductRepository>();
// builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();

// TODO: Add JWT Authentication
// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(options =>
//     {
//         options.Authority = builder.Configuration["JwtSettings:Authority"];
//         options.Audience = builder.Configuration["JwtSettings:Audience"];
//     });

// TODO: Add Authorization policies
// builder.Services.AddAuthorization(options =>
// {
//     options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
// });

// TODO: Add CORS
// builder.Services.AddCors(options =>
// {
//     options.AddPolicy("AllowFrontend", policy =>
//         policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>())
//               .AllowAnyMethod()
//               .AllowAnyHeader()
//               .AllowCredentials());
// });

// TODO: Add Health Checks
// builder.Services.AddHealthChecks()
//     .AddNpgSql(builder.Configuration.GetConnectionString("CatalogDb")!)
//     .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

// TODO: Add OpenTelemetry for distributed tracing
// builder.Services.AddOpenTelemetry()
//     .WithTracing(tracing => tracing
//         .AddAspNetCoreInstrumentation()
//         .AddHttpClientInstrumentation()
//         .AddEntityFrameworkCoreInstrumentation()
//         .AddOtlpExporter(options => 
//             options.Endpoint = new Uri(builder.Configuration["Jaeger:Endpoint"]!)));

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

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// TODO: Add Serilog request logging
// app.UseSerilogRequestLogging();

// TODO: Add CORS
// app.UseCors("AllowFrontend");

app.UseHttpsRedirection();

// TODO: Add Authentication & Authorization
// app.UseAuthentication();
// app.UseAuthorization();

// TODO: Add Output Caching
// app.UseOutputCache();

// TODO: Map Minimal API endpoints
// app.MapProductEndpoints();
// app.MapCategoryEndpoints();

// TODO: Map Health Checks endpoint
// app.MapHealthChecks("/health");

app.Run();
