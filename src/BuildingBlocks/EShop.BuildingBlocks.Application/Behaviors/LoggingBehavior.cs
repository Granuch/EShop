using EShop.BuildingBlocks.Domain;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace EShop.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Pipeline behavior for logging all requests with performance monitoring
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private const int MaxDepth = 4;
    private const int MaxCollectionItems = 25;

    private static readonly HashSet<string> RedactedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password",
        "NewPassword",
        "CurrentPassword",
        "Token",
        "RefreshToken",
        "AccessToken",
        "Secret",
        "SecretKey",
        "ApiKey",
        "TwoFactorCode",
        "Code",
        "Otp",
        "Passcode",
        "ClientSecret",
        "ClientSecretValue",
        "SecurityToken",
        "AuthorizationCode",
        "RecoveryCode",
        "Pin"
    };

    private static readonly ConcurrentDictionary<Type, SafeLogPropertyMetadata[]> SafeLogMetadataCache = new();

    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString()[..8];
        var requestName = typeof(TRequest).Name;

        var safeRequest = SafeLog(request);

        _logger.LogInformation(
            "[{RequestId}] Handling {RequestName} {@Request}",
            requestId, requestName, safeRequest);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > 500)
            {
                _logger.LogWarning(
                    "[{RequestId}] Long running request: {RequestName} completed in {ElapsedMilliseconds}ms",
                    requestId, requestName, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation(
                    "[{RequestId}] Handled {RequestName} in {ElapsedMilliseconds}ms",
                    requestId, requestName, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "[{RequestId}] Request {RequestName} failed after {ElapsedMilliseconds}ms with error: {ErrorMessage}",
                requestId, requestName, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    private static object? SafeLog(object? obj, int depth = 0)
    {
        if (obj == null)
            return null;

        if (depth >= MaxDepth)
            return "[MaxDepthReached]";

        if (obj is ISafeLoggable loggable)
            return loggable.ToSafeLog();

        var type = obj.GetType();

        if (type.IsPrimitive ||
            obj is string ||
            obj is decimal ||
            obj is DateTime ||
            obj is DateTimeOffset ||
            obj is Guid ||
            obj is TimeSpan ||
            type.IsEnum)
            return obj;

        if (obj is SecretString)
            return "****";

        if (obj is IEnumerable enumerable)
        {
            var items = new List<object?>();
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count >= MaxCollectionItems)
                {
                    items.Add($"[TruncatedAfter:{MaxCollectionItems}]");
                    break;
                }

                items.Add(SafeLog(item, depth + 1));
                count++;
            }

            return items;
        }

        var metadata = SafeLogMetadataCache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetMethod is not null)
                .Select(p => new SafeLogPropertyMetadata(
                    p,
                    p.GetCustomAttribute<SensitiveDataAttribute>() != null || RedactedPropertyNames.Contains(p.Name)))
                .ToArray());

        var dict = new Dictionary<string, object?>(metadata.Length, StringComparer.Ordinal);
        foreach (var item in metadata)
        {
            object? value;
            try
            {
                value = item.Property.GetValue(obj);
            }
            catch
            {
                value = "[ReadError]";
            }

            dict[item.Property.Name] = item.IsSensitive ? "****" : SafeLog(value, depth + 1);
        }

        return dict;
    }

    private sealed record SafeLogPropertyMetadata(PropertyInfo Property, bool IsSensitive);
}

public interface ISafeLoggable
{
    object ToSafeLog();
}

public readonly struct SecretString
{
    private readonly string _value;
    public SecretString(string value) => _value = value;
    public override string ToString() => "****";
    public string GetValue() => _value;
}
