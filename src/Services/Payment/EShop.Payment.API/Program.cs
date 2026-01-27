var builder = WebApplication.CreateBuilder(args);

// TODO: Configure Serilog for structured logging
// builder.Host.UseSerilog((context, configuration) => 
//     configuration.ReadFrom.Configuration(context.Configuration));

// TODO: Add Payment Processor service (Mock or Real)
// builder.Services.AddScoped<IPaymentProcessor, MockPaymentProcessor>();

// TODO: Add MassTransit with RabbitMQ for consuming OrderCreatedEvent
// builder.Services.AddMassTransit(x =>
// {
//     // Register consumer
//     x.AddConsumer<OrderCreatedConsumer>();
//
//     x.UsingRabbitMq((context, cfg) =>
//     {
//         cfg.Host(builder.Configuration["RabbitMQ:Host"], h =>
//         {
//             h.Username(builder.Configuration["RabbitMQ:Username"]!);
//             h.Password(builder.Configuration["RabbitMQ:Password"]!);
//         });
//
//         // Configure retry policy for failed payments
//         cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
//
//         cfg.ConfigureEndpoints(context);
//     });
// });

// TODO: Add Polly for resilience (Circuit Breaker, Retry)
// builder.Services.AddResiliencePipeline("payment-pipeline", builder =>
// {
//     builder.AddRetry(new RetryStrategyOptions
//     {
//         MaxRetryAttempts = 3,
//         Delay = TimeSpan.FromSeconds(2)
//     });
//     builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
//     {
//         FailureRatio = 0.5,
//         SamplingDuration = TimeSpan.FromSeconds(30)
//     });
// });

// TODO: Add Health Checks
// builder.Services.AddHealthChecks()
//     .AddRabbitMQ(builder.Configuration["RabbitMQ:Host"]!);

// Add OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// TODO: Map Health Checks
// app.MapHealthChecks("/health");

app.Run();
