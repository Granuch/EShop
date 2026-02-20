using EShop.Ordering.API.Endpoints;
using EShop.Ordering.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
    optional: true,
    reloadOnChange: true);

// TODO: Configure Serilog for structured logging
// builder.Host.UseSerilog((context, configuration) => 
//     configuration.ReadFrom.Configuration(context.Configuration));

// TODO: Add DbContext with PostgreSQL
// builder.Services.AddDbContext<OrderingDbContext>(options =>
//     options.UseNpgsql(builder.Configuration.GetConnectionString("OrderingDb")));

// TODO: Add MediatR for CQRS
// builder.Services.AddMediatR(cfg => 
//     cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly));

// TODO: Add FluentValidation
// builder.Services.AddValidatorsFromAssembly(typeof(CreateOrderCommandValidator).Assembly);
// builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
// builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// TODO: Add repositories
// builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// TODO: Add MassTransit with RabbitMQ for event-driven communication
// builder.Services.AddMassTransit(x =>
// {
//     // Register consumers
//     x.AddConsumer<BasketCheckedOutConsumer>();
//     x.AddConsumer<PaymentSuccessConsumer>();
//     x.AddConsumer<PaymentFailedConsumer>();
//
//     x.UsingRabbitMq((context, cfg) =>
//     {
//         cfg.Host(builder.Configuration["RabbitMQ:Host"], h =>
//         {
//             h.Username(builder.Configuration["RabbitMQ:Username"]!);
//             h.Password(builder.Configuration["RabbitMQ:Password"]!);
//         });
//
//         cfg.ConfigureEndpoints(context);
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

// TODO: Add Health Checks
// builder.Services.AddHealthChecks()
//     .AddNpgSql(builder.Configuration.GetConnectionString("OrderingDb")!)
//     .AddRabbitMQ(builder.Configuration["RabbitMQ:Host"]!);

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// TODO: Apply database migrations in Development
// if (app.Environment.IsDevelopment())
// {
//     using var scope = app.Services.CreateScope();
//     var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
//     await dbContext.Database.MigrateAsync();
// }

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// TODO: Add Authentication & Authorization
// app.UseAuthentication();
// app.UseAuthorization();

// TODO: Map Order endpoints
// app.MapOrderEndpoints();

// TODO: Map Health Checks
// app.MapHealthChecks("/health");

app.Run();
