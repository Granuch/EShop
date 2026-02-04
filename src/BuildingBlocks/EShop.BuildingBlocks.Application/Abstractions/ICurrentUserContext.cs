namespace EShop.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Provides access to the current user context.
/// Works in HTTP requests, background services, and integration tests.
/// Thread-safe and deterministic.
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>
    /// Gets the current user's ID, or null if no user is authenticated.
    /// In background services, returns "system".
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets the current user's username/email, or null if not available.
    /// </summary>
    string? UserName { get; }

    /// <summary>
    /// Indicates whether the current context has an authenticated user.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the correlation ID for the current request/operation.
    /// Used for distributed tracing.
    /// </summary>
    string CorrelationId { get; }
}

/// <summary>
/// Represents a system-level context for background operations.
/// Immutable and safe for concurrent access.
/// </summary>
public sealed class SystemUserContext : ICurrentUserContext
{
    public static readonly SystemUserContext Instance = new();

    private SystemUserContext() { }

    public string? UserId => "system";
    public string? UserName => "System";
    public bool IsAuthenticated => false;
    public string CorrelationId { get; } = $"system-{Guid.NewGuid():N}";
}
