using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Http;
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
            IOptions<StripeSettings> stripeOptions,
            CancellationToken cancellationToken) =>
        {
            if (!stripeOptions.Value.Enabled)
            {
                return ProblemResults.For(
                    "STRIPE_NOT_ENABLED",
                    "Stripe payments are not enabled.",
                    StatusCodes.Status503ServiceUnavailable);
            }

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
                error => ProblemResults.For(
                    error,
                    error.Code == "PAYMENT_ALREADY_EXISTS"
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
                error => ProblemResults.For(
                    error,
                    error.Code == "PAYMENT_ALREADY_EXISTS"
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
                return ProblemResults.For(result.Error!, StatusCodes.Status404NotFound);
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
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithTags("Payments")
        .WithName("GetPaymentsByUser")
        .RequireAuthorization("SameUserOrAdmin")
        .Produces<List<PaymentResponse>>(StatusCodes.Status200OK);

        // Ordering audit Stage 11. Admin only: a refund is a manual Payment operation. This used to accept
        // the payment's owner as well, and required only a successful payment, so a customer could refund
        // their own payment at any time — including after the order had shipped.
        group.MapPost("/{id:guid}/refund", async (
            Guid id,
            RefundPaymentRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new RefundPaymentCommand(id, request.Amount, request.Reason), cancellationToken);

            return result.Match(
                value => Results.Ok(ToResponse(value)),
                error => ProblemResults.For(
                    error,
                    error.Code switch
                    {
                        "PAYMENT_NOT_FOUND" => StatusCodes.Status404NotFound,
                        "PAYMENT_ALREADY_PROCESSED" => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status400BadRequest
                    }));
        })
        .WithName("RefundPayment")
        .RequireAuthorization("Admin")
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
            IOptions<StripeSettings> stripeOptions,
            IStripeWebhookProcessor webhookProcessor,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("PaymentEndpoints");
            var stripeSettings = stripeOptions.Value;

            if (!stripeSettings.Enabled)
            {
                return Results.NotFound();
            }

            if (!request.Headers.TryGetValue("Stripe-Signature", out var signatureHeader)
                || string.IsNullOrWhiteSpace(signatureHeader))
            {
                if (!stripeSettings.SkipWebhookSignatureVerification
                    || !stripeSettings.AllowMissingSignatureHeaderInBypassMode)
                {
                    return ProblemResults.For(
                        "STRIPE_SIGNATURE_MISSING",
                        "Missing Stripe-Signature header.",
                        StatusCodes.Status400BadRequest);
                }

                signatureHeader = string.Empty;
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
                return ProblemResults.For(
                    "STRIPE_PAYLOAD_EMPTY",
                    "Webhook payload is empty.",
                    StatusCodes.Status400BadRequest);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsStripeWebhookPayloadOrSignatureError(ex))
            {
                // Detail carries Stripe's own message. That is a third-party library string
                // rather than one of ours, so it sits on the wrong side of the "our strings yes,
                // framework strings no" rule - but it is pre-existing behaviour and useful to a
                // webhook integrator, so it is preserved here rather than changed silently.
                return ProblemResults.For(
                    "STRIPE_WEBHOOK_INVALID",
                    ex.Message,
                    StatusCodes.Status400BadRequest);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stripe webhook processing failed due to internal error.");
                return ProblemResults.For(
                    "STRIPE_WEBHOOK_PROCESSING_FAILED",
                    "Failed to process Stripe webhook due to an internal error.",
                    StatusCodes.Status500InternalServerError);
            }
        })
        .WithTags("Stripe Webhooks")
        .WithName("StripeWebhook")
        .AllowAnonymous()
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    private static bool IsStripeWebhookPayloadOrSignatureError(Exception ex)
    {
        if (ex is ArgumentException { Message: "Invalid Stripe webhook signature." })
        {
            return true;
        }

        return ex is InvalidOperationException { Message: "Stripe webhook payload parsing failed." };
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
            error = ProblemResults.For(
                "Unauthorized",
                "User identifier not found in authentication claims.",
                StatusCodes.Status401Unauthorized);
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
