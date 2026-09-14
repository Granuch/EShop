using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.Infrastructure.Http;

public static class ProblemDetailsExceptionMiddlewareExtensions
{
    /// <summary>
    /// Registers the canonical problem-details pipeline. <paramref name="configure"/> selects the
    /// service's exception branches; pass nothing for a fallback-only handler (the gateway).
    /// </summary>
    public static IServiceCollection AddEShopProblemDetails(
        this IServiceCollection services,
        Action<ProblemDetailsExceptionOptions>? configure = null)
    {
        // Reaches framework-generated problem responses - notably the [ApiController] automatic
        // 400 for model-binding failures - so those carry traceId and errorCode too. It does NOT
        // reach Results.Problem: ProblemHttpResult serialises directly without consulting
        // IProblemDetailsService, which is why ProblemResults attaches the extensions itself.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions.TryAdd(EShopProblem.TraceIdKey, context.HttpContext.TraceIdentifier);

            if (context.ProblemDetails is ValidationProblemDetails)
            {
                context.ProblemDetails.Extensions.TryAdd(EShopProblem.ErrorCodeKey, ProblemErrorCodes.ValidationError);
            }
        });

        services.Configure<ProblemDetailsExceptionOptions>(options => configure?.Invoke(options));

        return services;
    }

    /// <summary>
    /// Name kept from the per-service middlewares this replaces, so Program.cs call sites did not
    /// need to change - only their using directive.
    /// </summary>
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<ProblemDetailsExceptionMiddleware>();
}
