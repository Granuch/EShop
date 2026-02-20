using EShop.Notification.API;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
    optional: true,
    reloadOnChange: true);

// TODO: Configure Serilog for structured logging
// builder.Services.AddSerilog((context, configuration) => 
//     configuration.ReadFrom.Configuration(context.Configuration));

// TODO: Add Email service
// builder.Services.AddScoped<IEmailService, EmailService>();

// TODO: Configure SMTP settings
// builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));

// TODO: Add Template Rendering (RazorLight or Scriban)
// builder.Services.AddScoped<ITemplateRenderer, RazorTemplateRenderer>();

// TODO: Add MassTransit with RabbitMQ for consuming notification events
// builder.Services.AddMassTransit(x =>
// {
//     // Register consumers
//     x.AddConsumer<OrderCreatedConsumer>();
//     x.AddConsumer<OrderShippedConsumer>();
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
//         // Configure retry policy for failed email sends
//         cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
//
//         cfg.ConfigureEndpoints(context);
//     });
// });

// TODO: Add Health Checks
// builder.Services.AddHealthChecks()
//     .AddRabbitMQ(builder.Configuration["RabbitMQ:Host"]!);

// Optional: Add Worker service for background processing
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
