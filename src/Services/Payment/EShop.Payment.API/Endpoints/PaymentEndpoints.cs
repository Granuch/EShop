using EShop.BuildingBlocks.Domain;
using EShop.Payment.API.Infrastructure.Security;
using EShop.Payment.Application.Payments.Commands.CreatePayment;
using EShop.Payment.Application.Payments.Commands.RefundPayment;
using EShop.Payment.Application.Payments.Common;
using EShop.Payment.Application.Payments.Queries.GetPaymentById;
using EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;
using MediatR;
using System.Security.Claims;

namespace EShop.Payment.API.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapPost("/", async (
            CreatePaymentRequest request,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var subjectId = user.GetSubjectId();
            if (!user.IsAdmin() &&
                !string.IsNullOrWhiteSpace(subjectId) &&
                !string.Equals(subjectId, request.UserId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Forbid();
            }

            var result = await mediator.Send(new CreatePaymentCommand(
                request.OrderId,
                request.UserId,
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
            var result = await mediator.Send(new GetPaymentByIdQuery(id), cancellationToken);

            if (result.IsFailure)
            {
                return Results.Problem(
                    detail: result.Error!.Message,
                    title: result.Error.Code,
                    statusCode: StatusCodes.Status404NotFound);
            }

            var payment = result.Value!;

            var subjectId = user.GetSubjectId();
            if (!user.IsAdmin() &&
                !string.IsNullOrWhiteSpace(subjectId) &&
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
            var paymentResult = await mediator.Send(new GetPaymentByIdQuery(id), cancellationToken);
            if (paymentResult.IsFailure)
            {
                return Results.Problem(
                    detail: paymentResult.Error!.Message,
                    title: paymentResult.Error.Code,
                    statusCode: StatusCodes.Status404NotFound);
            }

            var payment = paymentResult.Value!;

            var subjectId = user.GetSubjectId();
            if (!user.IsAdmin() &&
                !string.IsNullOrWhiteSpace(subjectId) &&
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
}

public sealed record CreatePaymentRequest(
    Guid OrderId,
    string UserId,
    decimal Amount,
    string? Currency,
    string? PaymentMethod);

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
