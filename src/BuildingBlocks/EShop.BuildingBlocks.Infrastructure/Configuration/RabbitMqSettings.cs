namespace EShop.BuildingBlocks.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for RabbitMQ connection.
/// Bound from "RabbitMQ" configuration section.
/// </summary>
public sealed class RabbitMqSettings
{
    public const string SectionName = "RabbitMQ";

    /// <summary>
    /// RabbitMQ host address (e.g., "localhost" or "rabbitmq").
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// RabbitMQ port. Default is 5672.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// RabbitMQ username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// RabbitMQ password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Virtual host. Default is "/".
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Whether to use TLS/SSL for the RabbitMQ connection.
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Number of concurrent consumers per endpoint.
    /// </summary>
    public int ConcurrencyLimit { get; set; } = 10;

    /// <summary>
    /// Number of messages prefetched from the broker per endpoint.
    /// Higher values improve throughput at the cost of memory.
    /// </summary>
    public int PrefetchCount { get; set; } = 16;

    /// <summary>
    /// Number of retry attempts before sending to error queue.
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Initial retry interval in seconds.
    /// </summary>
    public int RetryIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Increment added to each subsequent retry interval in seconds.
    /// Produces delays: initial, initial+increment, initial+2*increment, etc.
    /// </summary>
    public int RetryIncrementSeconds { get; set; } = 10;

    /// <summary>
    /// Circuit breaker: failure percentage (0-100) within the tracking period
    /// that triggers the circuit to open.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 50;

    /// <summary>
    /// Circuit breaker: minimum number of messages in the tracking period
    /// before the breaker can trip. Prevents tripping on low volume.
    /// </summary>
    public int CircuitBreakerActiveThreshold { get; set; } = 10;

    /// <summary>
    /// Circuit breaker: duration in seconds the circuit stays open.
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Connection heartbeat interval in seconds.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Cluster node hostnames for RabbitMQ HA deployments.
    /// When set, MassTransit uses cluster-aware connections for failover.
    /// Leave empty for single-node deployments.
    /// </summary>
    public string[] ClusterNodes { get; set; } = [];

    /// <summary>
    /// Whether to enable delayed redelivery for longer backoff after immediate retries are exhausted.
    /// Requires delayed exchange plugin support when using RabbitMQ transport-level delayed redelivery.
    /// </summary>
    public bool UseDelayedRedelivery { get; set; } = true;

    /// <summary>
    /// Enables RabbitMQ delayed-exchange plugin specific topology (x-delayed-message).
    /// Set to true only when the broker has rabbitmq_delayed_message_exchange enabled.
    /// </summary>
    public bool UseDelayedExchangePlugin { get; set; } = false;

    /// <summary>
    /// Delayed redelivery intervals in minutes.
    /// Applied after all immediate retries are exhausted.
    /// </summary>
    public int[] DelayedRedeliveryIntervalsMinutes { get; set; } = [5, 15, 30];

    /// <summary>
    /// Validates that required settings are present.
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);
}
