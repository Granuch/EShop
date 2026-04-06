namespace EShop.Payment.Application.Payments.Common;

public sealed record PaymentDto(
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
