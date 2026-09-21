using EShop.BuildingBlocks.Domain;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using FluentValidation;

namespace EShop.Notification.Application.Notifications.Queries;

/// <summary>
/// The filter surface the admin journal list (#70) and the journal stats (#77) share, bound from the query string with
/// <c>[AsParameters]</c>.
///
/// <para>
/// <b>Every value-typed property here is nullable, and that is load-bearing.</b> Under <c>[AsParameters]</c> a
/// non-nullable value type is a <i>required</i> parameter: a plain <c>bool HasError</c> would make every request that
/// omits it fail binding with a 400 claiming "the request body is not valid JSON", on a GET that has no body. That is
/// the single most common way to turn a suite red at once in this repo.
/// </para>
///
/// <para>
/// It is a base record rather than two copies of eight properties because a copy drifts: a stats page that quietly
/// stopped honouring <c>?templateName=</c> would still return a perfectly well-formed set of numbers for the wrong
/// rows. <c>[AsParameters]</c> binds inherited properties, which is what makes this work; it cannot <i>nest</i>, so
/// there is no third option.
/// </para>
///
/// <para>Uncached. Notification has no caching behaviors and registers no <c>ICacheKeyVersionProvider</c>, so a
/// declared cache family would evict nothing and log success — and a journal is read precisely when someone wants to
/// know what just happened.</para>
/// </summary>
public abstract record NotificationFilterQuery
{
    /// <summary>Repeatable: <c>?status=Failed&amp;status=Undeliverable</c>. A union, not an intersection.</summary>
    public string[]? Status { get; init; }

    /// <summary>The integration event's type name, e.g. <c>OrderCreatedEvent</c>. Case-insensitive.</summary>
    public string? EventType { get; init; }

    /// <summary>The email template, e.g. <c>OrderConfirmation</c>. Case-insensitive.</summary>
    public string? TemplateName { get; init; }

    /// <summary>Exact match on the recipient's user id.</summary>
    public string? UserId { get; init; }

    /// <summary>
    /// The address the email went to. Case-insensitive.
    /// </summary>
    /// <remarks>
    /// <c>[SensitiveData]</c> because <c>LoggingBehavior</c> logs the whole request object at Information and its
    /// name-based redaction list is a coincidence, not a contract — <c>Email</c> is not on it. This is personal data,
    /// not a credential, and the attribute covers both (Basket audit S10 marked a shipping address for the same reason).
    /// </remarks>
    [SensitiveData]
    public string? Email { get; init; }

    /// <summary>Inclusive lower bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Inclusive upper bound on <c>CreatedAt</c>. A value with no time zone is read as UTC.</summary>
    public DateTime? To { get; init; }

    /// <summary>True: only rows carrying a failure reason. False: only rows carrying none. Omitted: both.</summary>
    public bool? HasError { get; init; }

    /// <summary>What the query service narrows on. One place, so the list and the stats cannot diverge.</summary>
    public NotificationJournalFilter ToFilter() => new(
        NotificationQueryEnums.ParseStatuses(Status),
        EventType,
        TemplateName,
        UserId,
        Email,
        NotificationQueryEnums.AsUtc(From),
        NotificationQueryEnums.AsUtc(To),
        HasError);
}

/// <summary>
/// The rules both <see cref="NotificationFilterQuery"/>-derived queries need. A static helper rather than a shared
/// validator class, because FluentValidation's <c>Include</c> takes an <c>IValidator&lt;T&gt;</c> of the validated
/// type, and a validator registered for the base record would be resolved for no request and silently never run.
/// </summary>
internal static class NotificationFilterRules
{
    private static readonly string[] StatusNames = Enum.GetNames<NotificationStatus>();

    public static void Apply<T>(AbstractValidator<T> validator) where T : NotificationFilterQuery
    {
        // By name only. Enum.TryParse also accepts "7" or "-1", which would reach the query as a status no
        // notification can have and return an empty page rather than an error.
        validator.RuleForEach(x => x.Status)
            .Must(status => StatusNames.Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Status must each be one of: {string.Join(", ", StatusNames)}")
            .When(x => x.Status is { Length: > 0 });

        validator.RuleFor(x => x.EventType)
            .MaximumLength(200).WithMessage("EventType must not exceed 200 characters.");

        validator.RuleFor(x => x.TemplateName)
            .MaximumLength(100).WithMessage("TemplateName must not exceed 100 characters.");

        validator.RuleFor(x => x.UserId)
            .MaximumLength(100).WithMessage("UserId must not exceed 100 characters.");

        validator.RuleFor(x => x.Email)
            .MaximumLength(320).WithMessage("Email must not exceed 320 characters.");

        validator.RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .WithMessage("To must not be earlier than From")
            .When(x => x.From.HasValue && x.To.HasValue);
    }
}
