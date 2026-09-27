using EShop.BuildingBlocks.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// The <c>[ApiController]</c> automatic 400 (a body that is not JSON, a <c>null</c> for a required string, a query value
/// of the wrong type) on the same envelope as every other validation failure (frontend-contracts F-03): <c>errorCode</c>
/// <c>ValidationError</c>, the messages in <c>detail</c>, and <c>errors</c> keyed by camelCase name. The framework's
/// default put the message in <c>title</c>, left out <c>detail</c> and kept its keys as bound.
/// </summary>
public static class EShopMvcValidation
{
    public const string FallbackMessage = "The value is not valid.";

    public static IActionResult InvalidModelStateResponse(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .GroupBy(
                entry => FieldValidationError.KeyFor(entry.Key),
                entry => entry.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? FallbackMessage : e.ErrorMessage))
            .ToDictionary(g => g.Key, g => g.SelectMany(messages => messages).Distinct().ToArray());

        var error = new FieldValidationError(errors);

        // MVC's own factory fills type, title and the W3C traceId, as Results.Problem does for the minimal-API services.
        var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(context.HttpContext, StatusCodes.Status400BadRequest, detail: error.Message);
        problem.Extensions[EShopProblem.ErrorCodeKey] = error.Code;
        problem.Extensions[EShopProblem.ErrorsKey] = error.Errors;

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }
}
