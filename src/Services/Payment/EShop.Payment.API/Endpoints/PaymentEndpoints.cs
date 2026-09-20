using EShop.BuildingBlocks.Application.Pagination;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Payment.API.Infrastructure.Export;
using EShop.Payment.API.Infrastructure.Security;
using EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;
using EShop.Payment.Application.Payments.Commands.CreatePayment;
using EShop.Payment.Application.Payments.Commands.RefundPayment;
using EShop.Payment.Application.Payments.Commands.ReplayFailedStripeWebhooks;
using EShop.Payment.Application.Payments.Commands.SettleOfflinePayment;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Application.Payments.Queries.ExportPayments;
using EShop.Payment.Application.Payments.Queries.GetPaymentById;
using EShop.Payment.Application.Payments.Queries.GetPaymentEvents;
using EShop.Payment.Application.Payments.Queries.GetPayments;
using EShop.Payment.Application.Payments.Queries.GetPaymentStats;
using EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace EShop.Payment.API.Endpoints;

/// <summary>
/// Payment audit Stage 12. Every payment endpoint answers <see cref="PaymentDto"/> itself. The API used to copy it, field
/// for field, into a <c>PaymentResponse</c>, and the copy re-applied the upper-casing and the empty-intent-to-null
/// mapping that <c>ToDto</c> had already done. The JSON is unchanged.
/// </summary>
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

            try
            {
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
            }
            catch (PaymentProviderUnavailableException)
            {
                // Payment audit Stage 6 (H2). The transaction has rolled back, so the payment is still Pending and the
                // same request can be retried. TransactionBehavior has logged the exception.
                return ProblemResults.For(
                    "PAYMENT_PROVIDER_UNAVAILABLE",
                    "The payment provider is unavailable. Retry shortly.",
                    StatusCodes.Status503ServiceUnavailable);
            }
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
                value => Results.Ok(value),
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
        .Produces<PaymentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // Admin panel S10 (endpoint #58, decision Q6a). An operator records an order paid outside the system. Payment's
        // own record is settled as a Mock payment and the existing PaymentSuccessEvent reaches Order.MarkAsPaid by the
        // one path that already exists — so Ordering gains no second way to mark an order paid, and the order turns
        // Paid one outbox poll later, exactly as a Stripe payment does.
        group.MapPost("/offline", async (
            SettleOfflinePaymentRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(
                new SettleOfflinePaymentCommand(request.OrderId, request.Reference ?? string.Empty),
                cancellationToken);

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(
                    error,
                    error.Code switch
                    {
                        "PAYMENT_NOT_FOUND" => StatusCodes.Status404NotFound,
                        "PAYMENT_NOT_PENDING" or "PAYMENT_REFERENCE_IN_USE" => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status400BadRequest
                    }));
        })
        .WithName("SettleOfflinePayment")
        // The permission, not the Admin role (decision Q4c, and EShopPermissions.PaymentsWrite names this endpoint in
        // its own doc comment). Behaviour is unchanged for every existing caller: the Admin role bundles every
        // permission. Payment's three older admin endpoints keep RequireAuthorization("Admin") and migrate when a
        // stage touches them, which is the repo's stated rule for this transition.
        .RequireAuthorization(EShopPermissions.PaymentsWrite)
        .Produces<PaymentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // Admin panel S10 (endpoint #65). The first cross-user payment list this service has ever had. Same paged
        // PaymentDto shape as GET /api/v1/users/{userId}/payments, rather than a second projection.
        group.MapGet("/", async (
            [AsParameters] GetPaymentsQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query, cancellationToken);

            return result.Match(
                page => Results.Ok(page),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetPayments")
        .RequireAuthorization(EShopPermissions.PaymentsRead)
        .Produces<PagedResult<PaymentDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // Admin panel S10 (endpoint #68). Declared before "/{id:guid}" for readability only — the route constraint is
        // what keeps "stats" and "export" from being read as payment ids, not the declaration order.
        group.MapGet("/stats", async (
            [AsParameters] GetPaymentStatsQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query, cancellationToken);

            return result.Match(
                stats => Results.Ok(stats),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetPaymentStats")
        .RequireAuthorization(EShopPermissions.PaymentsRead)
        .Produces<PaymentStatsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // Admin panel S10 (endpoint #69). Hard-capped rather than truncated (risk A8) — see ExportPaymentsQuery.MaxRows.
        group.MapGet("/export", async (
            [AsParameters] ExportPaymentsQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query, cancellationToken);

            return result.Match(
                payments => Results.File(
                    // UTF-8 with a BOM: without it Excel reads the file as the machine's ANSI code page, and a
                    // non-ASCII decline reason or user id arrives mangled.
                    System.Text.Encoding.UTF8.GetPreamble()
                        .Concat(System.Text.Encoding.UTF8.GetBytes(PaymentCsvWriter.Write(payments)))
                        .ToArray(),
                    "text/csv",
                    $"payments-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv"),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("ExportPayments")
        .RequireAuthorization(EShopPermissions.PaymentsRead)
        .Produces<string>(StatusCodes.Status200OK, "text/csv")
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // Admin panel S11 (endpoint #67). Declared before "/{id:guid}" for readability only — "webhooks" cannot be
        // read as a payment id, because of the route constraint rather than the declaration order.
        group.MapPost("/webhooks/failed/replay", async (
            ReplayFailedStripeWebhooksRequest? request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(
                new ReplayFailedStripeWebhooksCommand(request?.Ids), cancellationToken);

            return result.Match(
                report => Results.Ok(report),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("ReplayFailedStripeWebhooks")
        // payments.write, not system.manage: a replay re-applies a payment outcome, so it changes money records. It
        // is the same authority POST /payments/offline needs, and deliberately not payments.refund, which is held back
        // for the one action that moves money outward.
        .RequireAuthorization(EShopPermissions.PaymentsWrite)
        .Produces<FailedStripeWebhookReplayDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // Admin panel S11 (endpoint #66). Admin-only, unlike GET /{id} beside it: the timeline names operators,
        // carries Stripe event ids and quotes decline reasons, none of which is a customer's business.
        group.MapGet("/{id:guid}/events", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new GetPaymentEventsQuery(id), cancellationToken);

            return result.Match(
                events => Results.Ok(events),
                error => ProblemResults.For(error, StatusCodes.Status404NotFound));
        })
        .WithName("GetPaymentEvents")
        .RequireAuthorization(EShopPermissions.PaymentsRead)
        .Produces<IReadOnlyList<PaymentEventDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

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

            // Payment audit Stage 12. The query answers "not found" for another customer's payment, as /create-intent
            // does. This endpoint used to load the payment and answer 403, which confirmed that the id existed.
            var result = await mediator.Send(new GetPaymentByIdQuery(id, subjectId, user.IsAdmin()), cancellationToken);

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(error, StatusCodes.Status404NotFound));
        })
        .WithName("GetPaymentById")
        .RequireAuthorization()
        .Produces<PaymentDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // Payment audit Stage 10 (M7, D10). Paged like Ordering's per-user order list: ?pageNumber (default 1) and
        // ?pageSize (default 10, at most 100). It used to return every payment the user ever had, as a bare array.
        app.MapGet("/api/v1/users/{userId}/payments", async (
            string userId,
            [AsParameters] GetPaymentsByUserQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query with { UserId = userId }, cancellationToken);

            return result.Match(
                page => Results.Ok(page),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithTags("Payments")
        .WithName("GetPaymentsByUser")
        .RequireAuthorization("SameUserOrAdmin")
        .Produces<PagedResult<PaymentDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

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
                value => Results.Ok(value),
                error => ProblemResults.For(
                    error,
                    error.Code switch
                    {
                        "PAYMENT_NOT_FOUND" => StatusCodes.Status404NotFound,
                        "PAYMENT_ALREADY_REFUNDED" or "PAYMENT_NOT_CAPTURED" => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status400BadRequest
                    }));
        })
        .WithName("RefundPayment")
        .RequireAuthorization("Admin")
        .Produces<PaymentDto>(StatusCodes.Status200OK)
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
            IFailedStripeWebhookStore failedWebhooks,
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
                // Admin panel S11 (endpoint #67). The delivery was good — it got past the signature check and the
                // parser, both of which throw StripeWebhookRejectedException above — and we lost it. Stripe does
                // redeliver a 500, but not forever and not on demand, so it is captured here for replay.
                //
                // Two things make this correct rather than convenient. It runs in the CATCH, so the failing unit of
                // work is finished with; and CaptureAsync writes through its own scope, because this one's DbContext
                // still holds the rejected changes and on Postgres its transaction is aborted. It never throws, so
                // the 500 Stripe needs is still the answer even when the capture itself fails.
                await failedWebhooks.CaptureAsync(payload, signatureHeader!, ex, cancellationToken);

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

    private static bool TryResolveUserContext(ClaimsPrincipal user, out string? subjectId, out IResult? error)
    {
        // Every caller is authenticated: the endpoints RequireAuthorization. What is left to check is that a customer's
        // token names the customer. Payment audit Stage 12 removed an unreachable "not authenticated" branch.
        subjectId = user.GetSubjectId();
        error = null;

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

/// <summary>
/// Admin panel S10 (endpoint #58). Only the order and the operator's evidence. There is no amount: the payment is
/// settled for what Payment recorded from <c>OrderCreatedEvent</c>, because an operator-supplied one would reach
/// <c>Order.MarkAsPaid</c>, which refuses any mismatch — so a typo would dead-letter the message in Ordering long
/// after this request answered 200.
/// </summary>
public sealed record SettleOfflinePaymentRequest(Guid OrderId, string? Reference);

/// <summary>
/// Admin panel S11 (endpoint #67). The whole body is optional: no body, or <c>{}</c>, means "every outstanding
/// capture, oldest first, up to the cap". <c>Ids</c> is nullable rather than <c>= []</c> because System.Text.Json
/// writes an omitted collection as an explicit <c>null</c>, which overwrites an initializer — the BUG-09 shape.
/// </summary>
public sealed record ReplayFailedStripeWebhooksRequest(IReadOnlyCollection<Guid>? Ids);

public sealed record PaymentSimulationDiagnosticsResponse(
    string Mode,
    int ProcessingDelayMinSeconds,
    int ProcessingDelayMaxSeconds,
    int SuccessRatePercent,
    int RefundDelaySeconds,
    int? RandomSeed,
    string ForcedFailureReason);
