using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using EShop.ApiGateway.AuditLog;
using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Health;
using EShop.ApiGateway.Middleware;
using EShop.ApiGateway.Notifications;
using EShop.ApiGateway.Simulation;
using EShop.ApiGateway.SystemAdmin;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Infrastructure.Hosting;
using EShop.BuildingBlocks.Infrastructure.Http;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using Serilog.Events;

ThreadPool.SetMinThreads(workerThreads: 100, completionPortThreads: 100);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
        optional: true,
        reloadOnChange: true);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "EShop.ApiGateway"));

    builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
    builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection(SimulationOptions.SectionName));
    builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
    builder.Services.Configure<EmailQueueHealthOptions>(builder.Configuration.GetSection(EmailQueueHealthOptions.SectionName));
    builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection(RateLimitingOptions.SectionName));
    builder.Services.Configure<IdentityProxyOptions>(builder.Configuration.GetSection(IdentityProxyOptions.SectionName));
    builder.Services.Configure<CatalogProxyOptions>(builder.Configuration.GetSection(CatalogProxyOptions.SectionName));
    builder.Services.Configure<OrderingProxyOptions>(builder.Configuration.GetSection(OrderingProxyOptions.SectionName));
    builder.Services.Configure<BasketProxyOptions>(builder.Configuration.GetSection(BasketProxyOptions.SectionName));
    builder.Services.Configure<NotificationProxyOptions>(builder.Configuration.GetSection(NotificationProxyOptions.SectionName));

    // Shared across every service — reads KnownNetworks as well as KnownProxies, which is what
    // works under Docker/Kubernetes, and logs rather than silently dropping an unparseable entry.
    var forwardedHeadersEnabled = builder.Services.AddEShopForwardedHeaders(builder.Configuration);

    var jwtSettings = builder.Configuration.GetSection("JwtSettings");
    // docker-ci DC-36: the shared guards every service already calls. The gateway used to check only length and
    // emptiness, so under the production override it started on the public CHANGE_ME key from .env.example while all
    // six services refused it. Both guards exempt Development and Testing only; Configuration/StartupGuardTests boots
    // this file as Production to prove the calls are still here.
    var jwtSecretKey = JwtSecretGuard.Validate(jwtSettings["SecretKey"], builder.Environment);
    var corsAllowedOrigins = CorsOriginGuard.GetValidatedOrigins(builder.Configuration, builder.Environment);

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
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
    });

    // Decision Q4c: one policy per permission, resolved from the caller's roles through
    // RolePermissionBundles, plus AdminArea ("holds any permission"). Every admin YARP route uses
    // AdminArea rather than the Admin role (frontend-contracts F-08/F-45): the gateway asks whether the
    // caller is an operator at all, and the service asks the exact question. There is deliberately no
    // "Admin" policy here any more, so a route that names it fails YARP's config validation at startup.
    builder.Services.AddEShopPermissions();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            // CorsOriginGuard has already refused an empty list outside Development and Testing, so an empty list
            // here means a local run with no frontend configured.
            if (corsAllowedOrigins.Length == 0)
            {
                policy.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithEShopExposedHeaders();
                return;
            }

            policy.WithOrigins(corsAllowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()
                .WithEShopExposedHeaders();
        });
    });

    builder.Services.AddRateLimiter(options =>
    {
        var settings = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();

        options.UseEShopRejectionResponse();
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: EShopForwardedHeaders.GetClientPartitionKey(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.GlobalPermitLimit,
                    Window = TimeSpan.FromSeconds(settings.GlobalWindowSeconds),
                    AutoReplenishment = true
                }));

        // AddFixedWindowLimiter(name, ...) has no partition key, so this was one bucket shared by
        // every caller. It is currently declared but unattached — no route calls RequireRateLimiting
        // or EnableRateLimiting("simulation") — so it has never actually throttled anything. Kept and
        // partitioned rather than deleted so that wiring it up later is safe by default; if it is
        // still unattached when the simulation feature is finished, delete it instead.
        options.AddPolicy<string>("simulation", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: EShopForwardedHeaders.GetClientPartitionKey(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.SimulationPermitLimit,
                    Window = TimeSpan.FromSeconds(settings.SimulationWindowSeconds),
                    AutoReplenishment = true
                }));
    });

    builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
    builder.Services.AddHttpClient();

    // Admin audit trail (S15): the gateway serves GET /api/v1/admin/audit itself, merging every audited service's own
    // trail. Bounded per service, so one hung service costs the page seconds, not the request timeout.
    builder.Services.AddHttpClient(AuditLogFanOut.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
    builder.Services.AddScoped<AuditLogFanOut>();

    // The System page (S19): aggregate health, read-only settings and read-only feature flags, served here. Same 5 s bound
    // per service as the audit fan-out, for the same reason.
    builder.Services.AddHttpClient(SystemFanOut.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
    builder.Services.AddScoped<SystemFanOut>();

    builder.Services.AddEShopOpenTelemetry(
        builder.Configuration,
        serviceName: "EShop.ApiGateway",
        serviceVersion: "1.0.0",
        environment: builder.Environment,
        additionalSources: "EShop.ApiGateway");

    builder.Services.AddSingleton<ISimulationProfileProvider, SimulationProfileProvider>();
    builder.Services.AddSingleton<ISimulationResponseFactory, SimulationResponseFactory>();

    builder.Services.AddSingleton<GatewayEmailQueue>();
    builder.Services.AddSingleton<IEmailNotificationService, EmailNotificationService>();
    builder.Services.AddSingleton<IEmailTemplateEngine, EmailTemplateEngine>();
    builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
    builder.Services.AddHostedService<GatewayEmailDispatcher>();

    // docker-ci DC-45: readiness answers "can this gateway instance serve traffic", not "is everything behind it up".
    // With downstream and smtp on the ready tag, one stopped database or a stopped mail catcher turned the gateway 503,
    // so any orchestrator routing on readiness would pull every gateway instance and cause a full outage while the
    // other routes still worked. Both checks stay on /health (all checks) for dashboards and alerts; a dead service
    // shows as 502/503 on its own routes. DownstreamHealthCheck still reports Unhealthy (see the gateway's CLAUDE.md).
    builder.Services.AddHealthChecks()
        .AddCheck<DownstreamHealthCheck>(DownstreamHealthCheck.Name, tags: ["dependency"])
        .AddCheck<SmtpGatewayHealthCheck>("smtp", tags: ["dependency"])
        .AddCheck<EmailQueueHealthCheck>("email-queue", tags: ["ready"])
        .AddCheck<GatewayLivenessHealthCheck>("gateway-liveness", tags: ["live"]);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();

    // The gateway proxies rather than executing domain logic, so it has never mapped
    // ValidationException/DomainException/UnauthorizedAccessException itself. Registering
    // AddCommon() here would silently reclassify a proxied UnauthorizedAccessException from 500.
    //
    // AddMalformedJsonBody() is the one branch it takes (frontend-contracts F-20), paired with ThrowOnBadRequest, for its
    // own endpoints: a query value of the wrong type on /api/v1/admin/audit was a bare 400 with an empty body.
    // BadHttpRequestException is always the client's fault, so the branch cannot reclassify a server failure.
    // Enums as PascalCase names, in and out (frontend-contracts F-01).
    builder.Services.AddEShopJson();
    builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
    builder.Services.AddEShopProblemDetails(options => options.AddMalformedJsonBody());

    var app = builder.Build();

    app.UseGlobalExceptionHandler();

    app.UseEShopForwardedHeaders(forwardedHeadersEnabled);

    app.UseEShopRequestLogging();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<EmailTriggerMiddleware>();
    app.UseCors("AllowFrontend");
    app.UseRateLimiter();

    var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORT"] ?? app.Configuration["HTTPS_PORT"];
    if (!string.IsNullOrWhiteSpace(httpsPort))
    {
        app.UseHttpsRedirection();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<IdentityProxyGuardMiddleware>();
    app.UseMiddleware<CatalogProxyGuardMiddleware>();
    app.UseMiddleware<OrderingProxyGuardMiddleware>();
    app.UseMiddleware<BasketProxyGuardMiddleware>();
    app.UseMiddleware<NotificationProxyGuardMiddleware>();

    app.UseMiddleware<SimulationDecisionMiddleware>();
    app.UseMiddleware<SimulationResponseMiddleware>();

    // OpenAPI: every environment except Production, the one rule all services share (Ordering audit L10,
    // EShopApiDocs). This was Development only.
    if (EShopApiDocs.IsExposedIn(app.Environment))
    {
        app.MapOpenApi();
    }

    app.MapReverseProxy();
    app.MapGatewayAuditLog();
    app.MapGatewaySystemEndpoints();

    // Both scrape endpoints are anonymous. Restricted to loopback + private networks unless
    // Metrics:AllowedNetworks says otherwise; Testing is exempt (TestServer has no socket).
    app.UseEShopMetricsAccess(app.Configuration, app.Environment);
    app.MapMetrics("/prometheus");
    app.UseEShopOpenTelemetryPrometheus();

    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = EShopHealthResponseWriter.WriteAsync
    });

    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = EShopHealthResponseWriter.WriteAsync
    });

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live"),
        ResponseWriter = EShopHealthResponseWriter.WriteAsync
    });

    app.MapGet("/", () => Results.Ok(new
    {
        service = "EShop API Gateway",
        version = "1.0.0",
        environment = app.Environment.EnvironmentName,
        endpoints = new
        {
            health = "/health",
            healthReady = "/health/ready",
            healthLive = "/health/live",
            metrics = new { prometheus = "/prometheus", otel = "/metrics" }
        }
    }));

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "API Gateway terminated unexpectedly");

    // docker-ci DC-37: exit code 1, not a rethrow that the runtime ends with signal 139; a test host or dotnet ef
    // still gets the exception (see EShopEntryPoint).
    return EShopEntryPoint.ExitCodeFor(ex, typeof(Program).Assembly);
}
finally
{
    Log.CloseAndFlush();
}

return 0;

public partial class Program;
