using EShop.BuildingBlocks.Infrastructure.Configuration;

namespace EShop.Basket.API.Infrastructure.Configuration;

/// <summary>
/// Basket audit S10 (M11). The Redis and RabbitMQ settings a deployed Basket connects with. <c>RabbitMqSettings.IsValid</c>
/// only checks that host, user and password are non-empty, so the tracked <c>appsettings.Production.json</c>'s
/// <c>#{RABBITMQ_HOST}#</c> passed it, and so did <c>#{REDIS_CONNECTION_STRING}#</c>: an unsubstituted deploy started and
/// failed on its first connection instead of at startup.
///
/// <para>The rule is <see cref="JwtSecretGuard"/>'s, with its pattern list: refused outside Development and Testing,
/// Sandbox included. A missing Redis connection string is refused everywhere — the tracked <c>appsettings.json</c> ships
/// an empty one, which the old <c>?? throw</c> let through because it is not null.</para>
/// </summary>
public static class BasketConfigurationGuard
{
    /// <summary>The settings checked for a placeholder.</summary>
    public static IReadOnlyList<string> CheckedSettings { get; } =
        ["ConnectionStrings:Redis", "RabbitMQ:Host", "RabbitMQ:Username", "RabbitMQ:Password"];

    /// <exception cref="InvalidOperationException">Redis is not configured, or a checked setting is a placeholder where one is not allowed.</exception>
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            throw new InvalidOperationException("ConnectionStrings:Redis is required.");
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return;
        }

        foreach (var key in CheckedSettings)
        {
            // A missing RabbitMQ setting is AddBasketMessaging's to refuse; this only rejects a value that is a placeholder.
            var value = configuration[key];
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            foreach (var pattern in JwtSecretGuard.PlaceholderPatterns)
            {
                if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{key} contains placeholder pattern '{pattern}'. Replace it with a real value before deploying to {environment.EnvironmentName}.");
                }
            }
        }
    }
}
