using EShop.Payment.Domain.Interfaces;
using FluentValidation;

namespace EShop.Payment.Application.Payments.Queries;

/// <summary>
/// The filter surface the admin payment list (#65) and the accounting export (#69) share, bound from the query string
/// with <c>[AsParameters]</c>.
///
/// <para>
/// <b>Every value-typed property here is nullable, and that is load-bearing.</b> Under <c>[AsParameters]</c> a
/// non-nullable value type is a <i>required</i> parameter: a plain <c>Guid OrderId</c> would make every request that
/// omits it fail binding with a 400 claiming "the request body is not valid JSON", on a GET that has no body. That is
/// the single most common way to turn a suite red at once in this repo.
/// </para>
///
/// <para>
/// It is a base record rather than two copies of nine properties because a copy drifts: an export that quietly stopped
/// honouring <c>?status=</c> would still return a perfectly well-formed CSV of the wrong rows.
/// </para>
///
/// <para>Uncached, like every other admin read added by this plan. Payment caches nothing at all today, and a cached
/// admin list would need a versioned family bumped by every webhook, consumer and settlement — a family nothing
/// currently bumps evicts nothing and logs success.</para>
/// </summary>
public abstract record PaymentFilterQuery
{
    /// <summary>Repeatable: <c>?status=Success&amp;status=Refunded</c>. A union, not an intersection.</summary>
    public string[]? Status { get; init; }

    /// <summary>Exact match on the payment's owner.</summary>
    public string? UserId { get; init; }

    /// <summary>Exact match on the order. A payment's order is unique, so this answers at most one row.</summary>
    public Guid? OrderId { get; init; }

    /// <summary>One of <c>None</c>, <c>Mock</c> or <c>Stripe</c>, case-insensitive.</summary>
    public string? PaymentMethod { get; init; }

    /// <summary>Inclusive lower bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Inclusive upper bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? To { get; init; }

    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }

    /// <summary>What the query service narrows on. One place, so the list and the export cannot diverge.</summary>
    public PaymentListFilter ToFilter() => new(
        PaymentQueryEnums.ParseStatuses(Status),
        UserId?.Trim(),
        OrderId,
        PaymentQueryEnums.ParseMethod(PaymentMethod),
        Currency: null,
        PaymentQueryEnums.AsUtc(From),
        PaymentQueryEnums.AsUtc(To),
        MinAmount,
        MaxAmount);
}

/// <summary>
/// The rules both <see cref="PaymentFilterQuery"/>-derived queries need. A static helper rather than a shared
/// validator class, because FluentValidation's <c>Include</c> takes an <c>IValidator&lt;T&gt;</c> of the validated
/// type, and a validator registered for the base record would be resolved for no request and silently never run.
/// </summary>
internal static class PaymentFilterRules
{
    private static readonly string[] StatusNames = Enum.GetNames<Domain.Entities.PaymentStatus>();
    private static readonly string[] MethodNames = Enum.GetNames<Domain.Entities.PaymentMethodType>();

    public static void Apply<T>(AbstractValidator<T> validator) where T : PaymentFilterQuery
    {
        // By name only. Enum.TryParse also accepts "7" or "-1", which would reach the query as a status no payment can
        // have and return an empty page rather than an error.
        validator.RuleForEach(x => x.Status)
            .Must(status => StatusNames.Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Status must each be one of: {string.Join(", ", StatusNames)}")
            .When(x => x.Status is { Length: > 0 });

        validator.RuleFor(x => x.PaymentMethod)
            .Must(method => MethodNames.Contains(method, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"PaymentMethod must be one of: {string.Join(", ", MethodNames)}")
            .When(x => !string.IsNullOrEmpty(x.PaymentMethod));

        validator.RuleFor(x => x.UserId)
            .MaximumLength(100).WithMessage("UserId must not exceed 100 characters.");

        validator.RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .WithMessage("To must not be earlier than From")
            .When(x => x.From.HasValue && x.To.HasValue);

        validator.RuleFor(x => x.MinAmount)
            .GreaterThanOrEqualTo(0).WithMessage("MinAmount must not be negative")
            .When(x => x.MinAmount.HasValue);

        validator.RuleFor(x => x.MaxAmount)
            .GreaterThanOrEqualTo(x => x.MinAmount!.Value)
            .WithMessage("MaxAmount must not be less than MinAmount")
            .When(x => x.MinAmount.HasValue && x.MaxAmount.HasValue);
    }
}
