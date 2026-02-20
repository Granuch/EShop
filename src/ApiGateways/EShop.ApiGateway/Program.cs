using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
    optional: true,
    reloadOnChange: true);

// TODO: Configure Serilog for structured logging
// builder.Host.UseSerilog((context, configuration) => 
//     configuration.ReadFrom.Configuration(context.Configuration));

// TODO: Add YARP Reverse Proxy
// builder.Services.AddReverseProxy()
//     .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// TODO: Add JWT Authentication for token validation
// var jwtSettings = builder.Configuration.GetSection("JwtSettings");
// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(options =>
//     {
//         options.TokenValidationParameters = new TokenValidationParameters
//         {
//             ValidateIssuer = true,
//             ValidateAudience = true,
//             ValidateLifetime = true,
//             ValidateIssuerSigningKey = true,
//             ValidIssuer = jwtSettings["Issuer"],
//             ValidAudience = jwtSettings["Audience"],
//             IssuerSigningKey = new SymmetricSecurityKey(
//                 Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!)),
//             ClockSkew = TimeSpan.Zero
//         };
//     });

// TODO: Add Authorization policies
// builder.Services.AddAuthorization(options =>
// {
//     options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
//     options.AddPolicy("admin", policy => policy.RequireRole("Admin"));
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

// TODO: Add Rate Limiting
// builder.Services.AddRateLimiter(options =>
// {
//     options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
//         RateLimitPartition.GetFixedWindowLimiter(
//             partitionKey: context.User.Identity?.Name ?? context.Request.Headers.Host.ToString(),
//             factory: partition => new FixedWindowRateLimiterOptions
//             {
//                 AutoReplenishment = true,
//                 PermitLimit = 100,
//                 Window = TimeSpan.FromMinutes(1)
//             }));
// });

// TODO: Add Health Checks for downstream services
// builder.Services.AddHealthChecks()
//     .AddUrlGroup(new Uri(builder.Configuration["Services:Identity:Url"]! + "/health"), "identity-api")
//     .AddUrlGroup(new Uri(builder.Configuration["Services:Catalog:Url"]! + "/health"), "catalog-api")
//     .AddUrlGroup(new Uri(builder.Configuration["Services:Basket:Url"]! + "/health"), "basket-api")
//     .AddUrlGroup(new Uri(builder.Configuration["Services:Ordering:Url"]! + "/health"), "ordering-api");

// TODO: Add Polly for resilience (Circuit Breaker, Retry)
// builder.Services.AddHttpClient("downstream")
//     .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30)))
//     .AddPolicyHandler(Policy<HttpResponseMessage>
//         .Handle<HttpRequestException>()
//         .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

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

// TODO: Add Rate Limiting
// app.UseRateLimiter();

// TODO: Add Authentication & Authorization
// app.UseAuthentication();
// app.UseAuthorization();

// TODO: Map YARP Reverse Proxy
// app.MapReverseProxy();

// TODO: Map Health Checks endpoint
// app.MapHealthChecks("/health");

app.Run();
