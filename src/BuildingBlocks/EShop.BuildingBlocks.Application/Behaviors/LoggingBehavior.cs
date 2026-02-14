using EShop.BuildingBlocks.Domain;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace EShop.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Pipeline behavior for logging all requests with performance monitoring
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
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

    private static object SafeLog(object obj)
    {
        if (obj == null) return null;

        if (obj is ISafeLoggable loggable)
            return loggable.ToSafeLog();

        var type = obj.GetType();

        if (type.IsPrimitive || obj is string || obj is decimal || obj is DateTime)
            return obj;

        if (obj is SecretString)
            return "****";

        if (obj is IEnumerable enumerable)
            return enumerable.Cast<object>().Select(SafeLog).ToList();

        var dict = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(
                prop => prop.Name,
                prop =>
                {
                    var isSensitive = prop.GetCustomAttribute<SensitiveDataAttribute>() != null;
                    var value = prop.GetValue(obj);
                    return isSensitive ? "****" : SafeLog(value);
                });

        return dict;
    }
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
