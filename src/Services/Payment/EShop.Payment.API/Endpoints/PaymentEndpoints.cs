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

            // Payment audit Stage 2 (C2, D4): the order is all the client names. Who owns it, and what it costs,
            // come from Payment's own record of the order.
            var result = await mediator.Send(new CreatePaymentIntentCommand(
                request.OrderId,
                subjectId,
                user.IsAdmin(),
                request.Email), cancellationToken);

            return result.Match(
                value => Results.Ok(new CreatePaymentIntentResponse(
                    value.PaymentId,
                    value.PaymentIntentId,
                    value.ClientSecret,
                    value.Status)),
                error => ProblemResults.For(
                    error,
                    error.Code switch
                    {
                        "PAYMENT_NOT_FOUND" => StatusCodes.Status404NotFound,
                        "PAYMENT_ALREADY_EXISTS" or "PAYMENT_NOT_READY" => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status400BadRequest
                    }));
        })
        .WithName("CreateStripePaymentIntent")
        .RequireAuthorization()
        .Produces<CreatePaymentIntentResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Payment audit Stage 3 (H4, D2). An admin tool: settles the order's recorded Pending payment through the
        // simulator, for the recorded amount. It used to be open to every customer and create a payment from the
        // request's user, amount, currency and method, so a customer could settle an order without Stripe.
        group.MapPost("/", async (
            SettlePaymentRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new CreatePaymentCommand(request.OrderId), cancellationToken);

            return result.Match(
                value => Results.Ok(ToResponse(value)),
                error => ProblemResults.For(
                    error,
                    error.Code switch
                    {
                        "PAYMENT_NOT_FOUND" => StatusCodes.Status404NotFound,
                        "PAYMENT_NOT_PENDING" => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status400BadRequest
                    }));
        })
        .WithName("CreatePayment")
        .RequireAuthorization("Admin")
        .Produces<PaymentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
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

                // Payment audit Stage 4 (H3). The endpoint is anonymous, and its answer used to say whether the event's
                // intent matched a payment and whether the event had been seen before, so anyone could probe for
                // intent ids. Stripe reads only the status code; the outcome goes to the log.
                logger.LogInformation(
                    "Stripe webhook {EventId} ({EventType}) processed: payment found {PaymentFound}, duplicate {IsDuplicate}.",
                    result.EventId,
                    result.EventType,
                    result.PaymentFound,
                    result.IsDuplicate);
                return Results.Ok();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (StripeWebhookRejectedException ex)
            {
                // Payment audit Stage 4 (M10). A typed rejection, with our own detail: this used to match exception
                // message text and echo Stripe's message, and a payload that failed to parse matched neither pattern,
                // so it was answered 500 and Stripe kept redelivering it.
                logger.LogWarning(ex, "Stripe webhook refused: {Reason}.", ex.Reason);
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
        // Payment audit Stage 3. Stripe answers a 429 by redelivering later, so throttling its deliveries only
        // delays payments being recorded. The endpoint is protected by the signature check instead.
        .DisableRateLimiting()
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
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

/// <summary>
/// Payment audit Stage 3 (H4, D2): only the order. The user, amount, currency and payment method it used to carry
/// are ignored if sent; the payment settled is the one recorded for the order.
/// </summary>
public sealed record SettlePaymentRequest(Guid OrderId);

/// <summary>
/// Payment audit Stage 2 (C2, D4): only the order. The user, amount and currency it used to carry are ignored if
/// sent; they come from Payment's record of the order.
/// </summary>
public sealed record CreatePaymentIntentRequest(
    Guid OrderId,
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
