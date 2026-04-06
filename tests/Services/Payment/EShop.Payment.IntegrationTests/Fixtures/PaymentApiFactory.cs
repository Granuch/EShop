using System.Text;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace EShop.Payment.IntegrationTests.Fixtures;

public class PaymentApiFactory : WebApplicationFactory<Program>
{
    internal const string TestJwtSecretKey = "THIS_IS_A_TEST_ONLY_SECRET_KEY_32_CHARS_MINIMUM";
    internal const string TestJwtIssuer = "ESHOP_PAYMENT_TEST_ISSUER";
    internal const string TestJwtAudience = "ESHOP_PAYMENT_TEST_AUDIENCE";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = TestJwtSecretKey,
                ["JwtSettings:Issuer"] = TestJwtIssuer,
                ["JwtSettings:Audience"] = TestJwtAudience,
                ["RabbitMQ:Host"] = string.Empty,
                ["RabbitMQ:Username"] = string.Empty,
                ["RabbitMQ:Password"] = string.Empty,
                ["PaymentSimulation:ProcessingDelayMinSeconds"] = "0",
                ["PaymentSimulation:ProcessingDelayMaxSeconds"] = "0",
                ["PaymentSimulation:RefundDelaySeconds"] = "0",
                ["PaymentSimulation:SuccessRatePercent"] = "100"
            };

            configBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = TestJwtIssuer,
                    ValidAudience = TestJwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecretKey)),
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };
            });

            services.RemoveAll<IPaymentProcessor>();
            services.AddScoped<IPaymentProcessor, MockPaymentProcessor>();

            services.RemoveAll<IPublishEndpoint>();
            services.AddSingleton(Mock.Of<IPublishEndpoint>());
        });
    }
}
