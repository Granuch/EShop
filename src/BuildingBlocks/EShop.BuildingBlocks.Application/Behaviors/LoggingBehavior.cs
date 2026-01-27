using MediatR;

namespace EShop.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Pipeline behavior for logging all requests
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // TODO: Inject ILogger
    // private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        // TODO: Log request start with correlation ID
        // _logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);

        // TODO: Start timer for performance monitoring
        // var stopwatch = Stopwatch.StartNew();

        var response = await next();

        // TODO: Log request completion with duration
        // stopwatch.Stop();
        // _logger.LogInformation("Handled {RequestName} in {ElapsedMilliseconds}ms", 
        //     typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
