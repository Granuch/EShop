using System.Reflection;
using EShop.BuildingBlocks.Domain;
using EShop.Notification.Application.Notifications.Queries;
using EShop.Notification.Application.Notifications.Queries.GetNotifications;
using EShop.Notification.Application.Notifications.Queries.GetNotificationStats;

namespace EShop.Notification.UnitTests.Queries;

/// <summary>
/// Admin panel S12. The journal's filter surface: what it refuses, what it lets through, and two structural facts that
/// nothing behavioural can catch.
/// </summary>
[TestFixture]
public class GetNotificationsQueryValidatorTests
{
    private readonly GetNotificationsQueryValidator _validator = new();

    [Test]
    public void AnEmptyQuery_IsValid_BecauseEveryFilterIsOptional()
    {
        Assert.That(_validator.Validate(new GetNotificationsQuery()).IsValid, Is.True);
    }

    [Test]
    public void EveryDefault_IsApplied_WhenNothingIsSupplied()
    {
        var query = new GetNotificationsQuery();

        Assert.Multiple(() =>
        {
            Assert.That(query.EffectivePageNumber, Is.EqualTo(1));
            Assert.That(query.EffectivePageSize, Is.EqualTo(20));
        });
    }

    [TestCase("Sent")]
    [TestCase("failed")]
    [TestCase("UNDELIVERABLE")]
    public void AStatusName_IsAcceptedWhateverItsCasing(string status)
    {
        Assert.That(_validator.Validate(new GetNotificationsQuery { Status = [status] }).IsValid, Is.True);
    }

    /// <summary>
    /// Enum.TryParse also accepts "2" and "-1", which would reach the query as a status no notification can have and
    /// answer a typo with an empty page rather than an error.
    /// </summary>
    [TestCase("Delivered")]
    [TestCase("2")]
    [TestCase("-1")]
    public void AValueThatIsNotAStatusName_IsRefused(string status)
    {
        var result = _validator.Validate(new GetNotificationsQuery { Status = [status] });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Select(e => e.ErrorMessage).First(), Does.Contain("Status must each be one of"));
    }

    [TestCase(0)]
    [TestCase(101)]
    public void APageSizeOutsideTheBounds_IsRefused_NamingThePropertyTheCallerSent(int pageSize)
    {
        var result = _validator.Validate(new GetNotificationsQuery { PageSize = pageSize });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Select(e => e.PropertyName), Does.Contain(nameof(GetNotificationsQuery.PageSize)),
            "the rule runs on EffectivePageSize, which no caller can set; the name must be overridden back");
    }

    [Test]
    public void AWindowThatEndsBeforeItStarts_IsRefused()
    {
        var result = _validator.Validate(new GetNotificationsQuery
        {
            From = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.That(result.IsValid, Is.False);
    }

    /// <summary>
    /// Under <c>[AsParameters]</c> a non-nullable value type is a <b>required</b> parameter: a plain <c>bool HasError</c>
    /// would make every request that omits it fail binding with a 400 claiming the request body is not valid JSON, on a
    /// GET that has no body. Structural, because no request this suite can send reaches the binder.
    /// </summary>
    [Test]
    public void EveryValueTypedFilterProperty_IsNullable()
    {
        var nonNullable = typeof(GetNotificationsQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Where(p => p.PropertyType.IsValueType && Nullable.GetUnderlyingType(p.PropertyType) is null)
            .Select(p => p.Name)
            .ToArray();

        Assert.That(nonNullable, Is.Empty,
            "a non-nullable value type here makes the parameter required and 400s every request that omits it");
    }

    /// <summary>
    /// <c>LoggingBehavior</c> logs the whole request object at Information and its name-based redaction list is a
    /// coincidence, not a contract — <c>Email</c> is not on it. The attribute is the only thing keeping a recipient's
    /// address out of Seq and the rolling log files.
    /// </summary>
    [Test]
    public void TheEmailFilter_IsMarkedSensitive()
    {
        var property = typeof(NotificationFilterQuery).GetProperty(nameof(NotificationFilterQuery.Email))!;

        Assert.That(property.GetCustomAttribute<SensitiveDataAttribute>(), Is.Not.Null);
    }

    /// <summary>
    /// The list and the stats share one filter record so they cannot drift — a stats page that quietly stopped
    /// honouring ?templateName= would still return a well-formed set of numbers for the wrong rows.
    /// </summary>
    [Test]
    public void BothJournalQueries_ShareTheOneFilterSurface()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new GetNotificationsQuery(), Is.InstanceOf<NotificationFilterQuery>());
            Assert.That(new GetNotificationStatsQuery(), Is.InstanceOf<NotificationFilterQuery>());
        });
    }
}
