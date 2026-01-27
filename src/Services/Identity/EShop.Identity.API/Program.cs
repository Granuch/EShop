using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// TODO: Configure Serilog for structured logging
// builder.Host.UseSerilog((context, configuration) => 
//     configuration.ReadFrom.Configuration(context.Configuration));

// TODO: Add DbContext with PostgreSQL
// builder.Services.AddDbContext<IdentityDbContext>(options =>
//     options.UseNpgsql(builder.Configuration.GetConnectionString("IdentityDb")));

// TODO: Add ASP.NET Core Identity
// builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
// {
//     options.Password.RequireDigit = true;
//     options.Password.RequireLowercase = true;
//     options.Password.RequireUppercase = true;
//     options.Password.RequireNonAlphanumeric = true;
//     options.Password.RequiredLength = 8;
//     options.User.RequireUniqueEmail = true;
//     options.SignIn.RequireConfirmedEmail = true;
//     options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
//     options.Lockout.MaxFailedAccessAttempts = 5;
// })
// .AddEntityFrameworkStores<IdentityDbContext>()
// .AddDefaultTokenProviders();

// TODO: Add MediatR for CQRS
// builder.Services.AddMediatR(cfg => 
//     cfg.RegisterServicesFromAssembly(typeof(RegisterCommand).Assembly));

// TODO: Add FluentValidation
// builder.Services.AddValidatorsFromAssembly(typeof(RegisterCommandValidator).Assembly);
// builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
// builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// TODO: Add services
// builder.Services.AddScoped<ITokenService, TokenService>();
// builder.Services.AddScoped<IUserRepository, UserRepository>();
// builder.Services.AddScoped<IEmailService, EmailService>();

// TODO: Configure JWT Authentication
// var jwtSettings = builder.Configuration.GetSection("JwtSettings");
// var secretKey = jwtSettings["SecretKey"];
// builder.Services.AddAuthentication(options =>
// {
//     options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
//     options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
// })
// .AddJwtBearer(options =>
// {
//     options.TokenValidationParameters = new TokenValidationParameters
//     {
//         ValidateIssuer = true,
//         ValidateAudience = true,
//         ValidateLifetime = true,
//         ValidateIssuerSigningKey = true,
//         ValidIssuer = jwtSettings["Issuer"],
//         ValidAudience = jwtSettings["Audience"],
//         IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!)),
//         ClockSkew = TimeSpan.Zero
//     };
// });

// TODO: Add OAuth providers (Google, GitHub)
// builder.Services.AddAuthentication()
//     .AddGoogle(options =>
//     {
//         options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
//         options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
//     })
//     .AddGitHub(options =>
//     {
//         options.ClientId = builder.Configuration["Authentication:GitHub:ClientId"]!;
//         options.ClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"]!;
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

// TODO: Add Rate Limiting
// builder.Services.AddRateLimiter(options =>
// {
//     options.AddFixedWindowLimiter("auth", opt =>
//     {
//         opt.PermitLimit = 10;
//         opt.Window = TimeSpan.FromMinutes(1);
//     });
// });

// TODO: Add Health Checks
// builder.Services.AddHealthChecks()
//     .AddNpgSql(builder.Configuration.GetConnectionString("IdentityDb")!);

// Add Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// TODO: Apply database migrations and seed data in Development
// if (app.Environment.IsDevelopment())
// {
//     using var scope = app.Services.CreateScope();
//     var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
//     await dbContext.Database.MigrateAsync();
//     
//     // Seed default roles and admin user
//     var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
//     var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
//     await SeedData.SeedRolesAndAdminAsync(roleManager, userManager);
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

// TODO: Add Rate Limiting
// app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// TODO: Map Controllers
// app.MapControllers();

// TODO: Map Health Checks
// app.MapHealthChecks("/health");

app.Run();
