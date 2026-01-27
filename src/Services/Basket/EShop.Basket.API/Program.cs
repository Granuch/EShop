using EShop.Basket.API.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// TODO: Configure Serilog for structured logging
// builder.Host.UseSerilog((context, configuration) => 
//     configuration.ReadFrom.Configuration(context.Configuration));

// TODO: Add Redis for basket storage
// builder.Services.AddStackExchangeRedisCache(options =>
// {
//     options.Configuration = builder.Configuration.GetConnectionString("Redis");
//     options.InstanceName = "Basket_";
// });

// TODO: Add MediatR for CQRS
// builder.Services.AddMediatR(cfg => 
//     cfg.RegisterServicesFromAssembly(typeof(AddItemToBasketCommand).Assembly));

// TODO: Add FluentValidation
// builder.Services.AddValidatorsFromAssembly(typeof(AddItemToBasketCommandValidator).Assembly);
// builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
// builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// TODO: Add repositories
// builder.Services.AddScoped<IBasketRepository, RedisBasketRepository>();

// TODO: Add MassTransit for publishing BasketCheckedOutEvent
// builder.Services.AddMassTransit(x =>
// {
//     x.UsingRabbitMq((context, cfg) =>
//     {
//         cfg.Host(builder.Configuration["RabbitMQ:Host"], h =>
//         {
//             h.Username(builder.Configuration["RabbitMQ:Username"]!);
//             h.Password(builder.Configuration["RabbitMQ:Password"]!);
//         });
//     });
// });

// TODO: Add JWT Authentication
// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(options =>
//     {
//         options.Authority = builder.Configuration["JwtSettings:Authority"];
//         options.Audience = builder.Configuration["JwtSettings:Audience"];
//     });

// TODO: Add Authorization
// builder.Services.AddAuthorization();

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
//     .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

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

// TODO: Map Basket endpoints
// app.MapBasketEndpoints();

// TODO: Map Health Checks
// app.MapHealthChecks("/health");

app.Run();
