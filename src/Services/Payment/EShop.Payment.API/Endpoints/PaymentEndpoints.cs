using EShop.BuildingBlocks.Domain;
using EShop.Payment.API.Infrastructure.Security;
using EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;
using EShop.Payment.Application.Payments.Commands.CreatePayment;
using EShop.Payment.Application.Payments.Commands.RefundPayment;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Application.Payments.Queries.GetPaymentById;
using EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Infrastructure.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace EShop.Payment.API.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapPost("/create-intent", async (
            CreatePaymentIntentRequest request,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(user, out var subjectId, out var authError))
            {
                return authError!;
            }

            if (!user.IsAdmin() &&
                !string.Equals(subjectId, request.UserId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            var resolvedUserId = user.IsAdmin() ? request.UserId : subjectId!;
            var result = await mediator.Send(new CreatePaymentIntentCommand(
                request.OrderId,
                resolvedUserId,
                request.Amount,
                request.Currency,
                request.Email), cancellationToken);

            return result.Match(
                value => Results.Ok(new CreatePaymentIntentResponse(
                    value.PaymentId,
                    value.PaymentIntentId,
                    value.ClientSecret,
                    value.Status)),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: error.Code == "PAYMENT_ALREADY_EXISTS"
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status400BadRequest));
        })
        .WithName("CreateStripePaymentIntent")
        .RequireAuthorization()
        .Produces<CreatePaymentIntentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/", async (
            CreatePaymentRequest request,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(user, out var subjectId, out var authError))
            {
                return authError!;
            }

            if (!user.IsAdmin() &&
                !string.Equals(subjectId, request.UserId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            var resolvedUserId = user.IsAdmin() ? request.UserId : subjectId!;

            var result = await mediator.Send(new CreatePaymentCommand(
                request.OrderId,
                resolvedUserId,
                request.Amount,
                request.Currency,
                request.PaymentMethod), cancellationToken);

            return result.Match(
                value => Results.Created($"/api/v1/payments/{value.Id}", ToResponse(value)),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: error.Code == "PAYMENT_ALREADY_EXISTS"
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status400BadRequest));
        })
        .WithName("CreatePayment")
        .RequireAuthorization()
        .Produces<PaymentResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(user, out var subjectId, out var authError))
            {
                return authError!;
            }

            var result = await mediator.Send(new GetPaymentByIdQuery(id), cancellationToken);

            if (result.IsFailure)
            {
                return Results.Problem(
                    detail: result.Error!.Message,
                    title: result.Error.Code,
                    statusCode: StatusCodes.Status404NotFound);
            }

            var payment = result.Value!;

            if (!user.IsAdmin() &&
                !string.Equals(subjectId, payment.UserId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            return Results.Ok(ToResponse(payment));
        })
        .WithName("GetPaymentById")
        .RequireAuthorization()
        .Produces<PaymentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/users/{userId}/payments", async (
            string userId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new GetPaymentsByUserQuery(userId), cancellationToken);

            return result.Match(
                value => Results.Ok(value.Select(ToResponse).ToList()),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: StatusCodes.Status400BadRequest));
        })
        .WithTags("Payments")
        .WithName("GetPaymentsByUser")
        .RequireAuthorization("SameUserOrAdmin")
        .Produces<List<PaymentResponse>>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/refund", async (
            Guid id,
            RefundPaymentRequest request,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(user, out var subjectId, out var authError))
            {
                return authError!;
            }

            var paymentResult = await mediator.Send(new GetPaymentByIdQuery(id), cancellationToken);
            if (paymentResult.IsFailure)
            {
                return Results.Problem(
                    detail: paymentResult.Error!.Message,
                    title: paymentResult.Error.Code,
                    statusCode: StatusCodes.Status404NotFound);
            }

            var payment = paymentResult.Value!;

            if (!user.IsAdmin() &&
                !string.Equals(subjectId, payment.UserId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            var result = await mediator.Send(new RefundPaymentCommand(id, request.Amount, request.Reason), cancellationToken);

            return result.Match(
                value => Results.Ok(ToResponse(value)),
                error => Results.Problem(
                    detail: error.Message,
                    title: error.Code,
                    statusCode: error.Code switch
                    {
                        "PAYMENT_NOT_FOUND" => StatusCodes.Status404NotFound,
                        "PAYMENT_ALREADY_PROCESSED" => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status400BadRequest
                    }));
        })
        .WithName("RefundPayment")
        .RequireAuthorization()
        .Produces<PaymentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/simulation", (IOptions<PaymentSimulationSettings> options) =>
        {
            var settings = options.Value;

            return Results.Ok(new PaymentSimulationDiagnosticsResponse(
                settings.Mode.ToString(),
                settings.ProcessingDelayMinSeconds,
                settings.ProcessingDelayMaxSeconds,
                settings.SuccessRatePercent,
                settings.RefundDelaySeconds,
                settings.RandomSeed,
                settings.ForcedFailureReason));
        })
        .WithName("GetPaymentSimulationDiagnostics")
        .RequireAuthorization("Admin")
        .Produces<PaymentSimulationDiagnosticsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/webhooks/stripe", async (
            HttpRequest request,
            IStripeWebhookProcessor webhookProcessor,
            CancellationToken cancellationToken) =>
        {
            if (!request.Headers.TryGetValue("Stripe-Signature", out var signatureHeader)
                || string.IsNullOrWhiteSpace(signatureHeader))
            {
                return Results.BadRequest(new { error = "Missing Stripe-Signature header." });
            }

            request.EnableBuffering();

            string payload;
            using (var reader = new StreamReader(request.Body, leaveOpen: true))
            {
                payload = await reader.ReadToEndAsync(cancellationToken);
            }

            request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(payload))
            {
                return Results.BadRequest(new { error = "Webhook payload is empty." });
            }

            try
            {
                var result = await webhookProcessor.ProcessAsync(payload, signatureHeader!, cancellationToken);
                return Results.Ok(new
                {
                    received = true,
                    result.IsDuplicate,
                    result.PaymentFound,
                    result.EventId,
                    result.EventType
                });
            }
            catch (ArgumentException ex) when (ex.Message == "Invalid Stripe webhook signature.")
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithTags("Stripe Webhooks")
        .WithName("StripeWebhook")
        .AllowAnonymous()
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }

    private static PaymentResponse ToResponse(PaymentDto payment)
    {
        return new PaymentResponse(
            payment.Id,
            payment.OrderId,
            payment.UserId,
            payment.Amount,
            payment.Currency,
            payment.PaymentMethod,
            payment.Status.ToString().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(payment.PaymentIntentId) ? null : payment.PaymentIntentId,
            payment.ErrorMessage,
            payment.CreatedAt,
            payment.ProcessedAt,
            payment.UpdatedAt);
    }

    private static bool TryResolveUserContext(ClaimsPrincipal user, out string? subjectId, out IResult? error)
    {
        subjectId = user.GetSubjectId();
        error = null;

        if (user.Identity?.IsAuthenticated != true)
        {
            error = Results.Unauthorized();
            return false;
        }

        if (user.IsAdmin())
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(subjectId))
        {
            error = Results.Problem(
                detail: "User identifier not found in authentication claims.",
                title: "Unauthorized",
                statusCode: StatusCodes.Status401Unauthorized);
            return false;
        }

        return true;
    }
}

public sealed record CreatePaymentRequest(
    Guid OrderId,
    string UserId,
    decimal Amount,
    string? Currency,
    string? PaymentMethod);

public sealed record CreatePaymentIntentRequest(
    Guid OrderId,
    string UserId,
    decimal Amount,
    string? Currency,
    string? Email);

public sealed record CreatePaymentIntentResponse(
    Guid PaymentId,
    string PaymentIntentId,
    string ClientSecret,
    string Status);

public sealed record RefundPaymentRequest(decimal? Amount, string? Reason);

public sealed record PaymentResponse(
    Guid Id,
    Guid OrderId,
    string UserId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Status,
    string? PaymentIntentId,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? ProcessedAt,
    DateTime? UpdatedAt);

public sealed record PaymentSimulationDiagnosticsResponse(
    string Mode,
    int ProcessingDelayMinSeconds,
    int ProcessingDelayMaxSeconds,
    int SuccessRatePercent,
    int RefundDelaySeconds,
    int? RandomSeed,
    string ForcedFailureReason);
