using EShop.Payment.Application.Payments.Queries.ExportPayments;
using EShop.Payment.Application.Payments.Queries.GetPayments;
using EShop.Payment.Application.Payments.Queries.GetPaymentStats;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Admin panel S10. What the admin read queries do before anything reaches the database: which values they refuse,
/// and what they hand the query service.
/// </summary>
[TestFixture]
public class AdminPaymentQueryTests
{
    private readonly GetPaymentsQueryValidator _list = new();
    private readonly ExportPaymentsQueryValidator _export = new();
    private readonly GetPaymentStatsQueryValidator _stats = new();

    // ---- Defaults ----

    [Test]
    public void AnEmptyListQuery_AsksForTheFirstPageOfTen_AndNarrowsNothing()
    {
        var query = new GetPaymentsQuery();

        var filter = query.ToFilter();
        Assert.Multiple(() =>
        {
            Assert.That(query.EffectivePageNumber, Is.EqualTo(1));
            Assert.That(query.EffectivePageSize, Is.EqualTo(10));
            Assert.That(filter.Statuses, Is.Empty);
            Assert.That(filter.UserId, Is.Null);
            Assert.That(filter.OrderId, Is.Null);
            Assert.That(filter.PaymentMethod, Is.Null);
            Assert.That(filter.From, Is.Null);
            Assert.That(filter.To, Is.Null);
        });
    }

    /// <summary>
    /// The list must not carry a currency of its own: each of its rows states one, and a default here would hide a
    /// payment in any other currency from the only screen that lists them all.
    /// </summary>
    [Test]
    public void TheListNarrowsNoCurrency_WhileTheStatsDefaultToUsd()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new GetPaymentsQuery().ToFilter().Currency, Is.Null);
            Assert.That(new ExportPaymentsQuery().ToFilter().Currency, Is.Null);
            Assert.That(new GetPaymentStatsQuery().EffectiveCurrency, Is.EqualTo("USD"));
            Assert.That(new GetPaymentStatsQuery { Currency = " eur " }.EffectiveCurrency, Is.EqualTo("EUR"));
        });
    }

    // ---- Parsing ----

    [Test]
    public void StatusNames_AreParsedCaseInsensitively_AndDeduped()
    {
        var filter = new GetPaymentsQuery { Status = ["success", "SUCCESS", "Refunded"] }.ToFilter();

        Assert.That(filter.Statuses, Is.EquivalentTo(new[] { PaymentStatus.Success, PaymentStatus.Refunded }));
    }

    [Test]
    public void AMethodName_IsParsedCaseInsensitively()
    {
        Assert.That(new GetPaymentsQuery { PaymentMethod = "stripe" }.ToFilter().PaymentMethod,
            Is.EqualTo(PaymentMethodType.Stripe));
    }

    /// <summary>
    /// Not cosmetic: <c>?from=2026-09-01</c> binds with <see cref="DateTimeKind.Unspecified"/>, and Npgsql refuses to
    /// send one as a <c>timestamp with time zone</c> parameter — so without this the most obvious filter on the screen
    /// is a 500, not a filter.
    /// </summary>
    [Test]
    public void ADateWithNoTimeZone_IsReadAsUtc()
    {
        var unspecified = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var filter = new GetPaymentsQuery { From = unspecified, To = unspecified.AddDays(1) }.ToFilter();

        Assert.Multiple(() =>
        {
            Assert.That(filter.From!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(filter.To!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(filter.From!.Value, Is.EqualTo(unspecified), "reading it as UTC must not shift the instant");
        });
    }

    [Test]
    public void ALocalDate_IsConvertedRatherThanRelabelled()
    {
        var local = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Local);

        var from = new GetPaymentsQuery { From = local }.ToFilter().From!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(from.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(from, Is.EqualTo(local.ToUniversalTime()));
        });
    }

    // ---- The list and the export share one filter surface ----

    /// <summary>
    /// The two queries are separate records because <c>[AsParameters]</c> cannot nest, and an export that quietly
    /// stopped honouring a filter would still return a perfectly well-formed CSV of the wrong rows. They share a base
    /// record; this asserts they have not been pulled apart into two lists that drift.
    /// </summary>
    [Test]
    public void TheExportAndTheList_NarrowOnExactlyTheSameThings()
    {
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var orderId = Guid.NewGuid();

        var list = new GetPaymentsQuery
        {
            Status = ["Failed"],
            UserId = " customer-9 ",
            OrderId = orderId,
            PaymentMethod = "Mock",
            From = from,
            To = from.AddDays(7),
            MinAmount = 5m,
            MaxAmount = 50m
        };
        var export = new ExportPaymentsQuery
        {
            Status = ["Failed"],
            UserId = " customer-9 ",
            OrderId = orderId,
            PaymentMethod = "Mock",
            From = from,
            To = from.AddDays(7),
            MinAmount = 5m,
            MaxAmount = 50m
        };

        var listFilter = list.ToFilter();
        var exportFilter = export.ToFilter();

        Assert.Multiple(() =>
        {
            // Record equality would compare Statuses by reference, so the collection is compared on its own.
            Assert.That(exportFilter.Statuses, Is.EquivalentTo(listFilter.Statuses!));
            Assert.That(exportFilter with { Statuses = null }, Is.EqualTo(listFilter with { Statuses = null }));
            Assert.That(listFilter.UserId, Is.EqualTo("customer-9"), "a pasted user id is trimmed");
            Assert.That(listFilter.Statuses, Is.EquivalentTo(new[] { PaymentStatus.Failed }));
            Assert.That(listFilter.PaymentMethod, Is.EqualTo(PaymentMethodType.Mock));
            Assert.That(listFilter.OrderId, Is.EqualTo(orderId));
            Assert.That(listFilter.MinAmount, Is.EqualTo(5m));
            Assert.That(listFilter.MaxAmount, Is.EqualTo(50m));
        });
    }

    // ---- Refusals ----

    [TestCase("Payed")]
    [TestCase("7")]
    [TestCase("-1")]
    public void AnUnknownStatusName_IsRefused_NotSilentlyDropped(string status)
    {
        Assert.Multiple(() =>
        {
            Assert.That(_list.Validate(new GetPaymentsQuery { Status = [status] }).IsValid, Is.False);
            Assert.That(_export.Validate(new ExportPaymentsQuery { Status = [status] }).IsValid, Is.False);
        });
    }

    /// <summary>One typo among several names must fail the request, not narrow it to the ones that parsed.</summary>
    [Test]
    public void OneBadNameAmongGoodOnes_IsStillRefused()
    {
        Assert.That(_list.Validate(new GetPaymentsQuery { Status = ["Success", "Payed", "Failed"] }).IsValid,
            Is.False);
    }

    [Test]
    public void AnUnknownPaymentMethod_IsRefused()
    {
        Assert.That(_list.Validate(new GetPaymentsQuery { PaymentMethod = "Cheque" }).IsValid, Is.False);
    }

    [TestCase(0)]
    [TestCase(101)]
    public void AnOutOfRangePageSize_IsRefused(int pageSize)
    {
        Assert.That(_list.Validate(new GetPaymentsQuery { PageSize = pageSize }).IsValid, Is.False);
    }

    [Test]
    public void AnInvertedDateRange_IsRefused()
    {
        var from = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);

        Assert.Multiple(() =>
        {
            Assert.That(_list.Validate(new GetPaymentsQuery { From = from, To = from.AddDays(-1) }).IsValid, Is.False);
            Assert.That(_stats.Validate(new GetPaymentStatsQuery { From = from, To = from.AddDays(-1) }).IsValid,
                Is.False);
        });
    }

    [Test]
    public void AnInvertedAmountRange_IsRefused()
    {
        Assert.That(_list.Validate(new GetPaymentsQuery { MinAmount = 50m, MaxAmount = 5m }).IsValid, Is.False);
    }

    [Test]
    public void ANegativeMinimumAmount_IsRefused()
    {
        Assert.That(_list.Validate(new GetPaymentsQuery { MinAmount = -1m }).IsValid, Is.False);
    }

    /// <summary>
    /// A dashboard asking for <c>week</c> and getting daily buckets labelled as weeks is worse than an error it can
    /// show. There is no week bucket, deliberately.
    /// </summary>
    [TestCase("week")]
    [TestCase("hour")]
    [TestCase("1")]
    public void AnUnknownGroupBy_IsRefused(string groupBy)
    {
        Assert.That(_stats.Validate(new GetPaymentStatsQuery { GroupBy = groupBy }).IsValid, Is.False);
    }

    [TestCase("Day")]
    [TestCase("month")]
    [TestCase("YEAR")]
    public void TheThreeBucketSizes_AreAccepted(string groupBy)
    {
        Assert.That(_stats.Validate(new GetPaymentStatsQuery { GroupBy = groupBy }).IsValid, Is.True);
    }

    [Test]
    public void ACurrencyThatIsNotThreeLetters_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_stats.Validate(new GetPaymentStatsQuery { Currency = "DOLLARS" }).IsValid, Is.False);
            Assert.That(_stats.Validate(new GetPaymentStatsQuery { Currency = "US" }).IsValid, Is.False);
            Assert.That(_stats.Validate(new GetPaymentStatsQuery { Currency = "usd" }).IsValid, Is.True);
        });
    }
}
