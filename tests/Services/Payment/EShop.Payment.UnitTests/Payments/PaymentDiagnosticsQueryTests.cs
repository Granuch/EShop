using EShop.Payment.Application.Payments.Commands.ReplayFailedStripeWebhooks;
using EShop.Payment.Application.Payments.Queries.GetPaymentEvents;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using Moq;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Admin panel S11: the timeline read and the replay command, before anything reaches a database.
/// </summary>
[TestFixture]
public class PaymentDiagnosticsQueryTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private Mock<IPaymentQueryService> _queryService = null!;
    private GetPaymentEventsQueryHandler _events = null!;

    [SetUp]
    public void SetUp()
    {
        _queryService = new Mock<IPaymentQueryService>();
        _events = new GetPaymentEventsQueryHandler(_queryService.Object);
    }

    private static PaymentEvent Row(PaymentStatus? from, PaymentStatus to, DateTime at, PaymentEventKind kind)
        => new(Guid.NewGuid(), kind, from, to, "detail", at, kind == PaymentEventKind.Webhook ? "evt_1" : null);

    /// <summary>
    /// Without the existence check an unknown id answers 200 with <c>[]</c>, which is indistinguishable from a payment
    /// nothing has happened to — so an operator who pasted the wrong id is told, convincingly, that the payment is
    /// quiet.
    /// </summary>
    [Test]
    public async Task AnUnknownPayment_IsNotFound_NotAnEmptyTimeline()
    {
        var id = Guid.NewGuid();
        _queryService.Setup(q => q.PaymentExistsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _events.Handle(new GetPaymentEventsQuery(id), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo("PAYMENT_NOT_FOUND"));
        });
        _queryService.Verify(q => q.GetEventsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task AKnownPaymentWithNoRows_IsAnEmptyTimeline_NotAnError()
    {
        var id = Guid.NewGuid();
        _queryService.Setup(q => q.PaymentExistsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _queryService.Setup(q => q.GetEventsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await _events.Handle(new GetPaymentEventsQuery(id), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Empty);
        });
    }

    /// <summary>
    /// The statuses reach the client as the same upper-case strings <c>PaymentDto.Status</c> uses, and the kind as its
    /// name — serializing either enum straight out would put a <b>number</b> in the response, so one payment endpoint
    /// would report <c>"SUCCESS"</c> and its timeline <c>2</c> for the same fact.
    /// </summary>
    [Test]
    public async Task TheDto_ReportsStatusesAndKindsAsStrings()
    {
        var id = Guid.NewGuid();
        _queryService.Setup(q => q.PaymentExistsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _queryService.Setup(q => q.GetEventsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Row(null, PaymentStatus.Pending, Now, PaymentEventKind.Transition),
            Row(PaymentStatus.Pending, PaymentStatus.Success, Now.AddMinutes(1), PaymentEventKind.Webhook)
        ]);

        var result = await _events.Handle(new GetPaymentEventsQuery(id), CancellationToken.None);

        var rows = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].FromStatus, Is.Null);
            Assert.That(rows[0].ToStatus, Is.EqualTo("PENDING"));
            Assert.That(rows[0].Kind, Is.EqualTo("Transition"));
            Assert.That(rows[0].StripeEventId, Is.Null);
            Assert.That(rows[1].FromStatus, Is.EqualTo("PENDING"));
            Assert.That(rows[1].ToStatus, Is.EqualTo("SUCCESS"));
            Assert.That(rows[1].Kind, Is.EqualTo("Webhook"));
            Assert.That(rows[1].StripeEventId, Is.EqualTo("evt_1"));
        });
    }

    /// <summary>The handler does not reorder: the query service owns the order, so a change there cannot be masked here.</summary>
    [Test]
    public async Task TheHandler_PreservesTheOrderItWasGiven()
    {
        var id = Guid.NewGuid();
        var first = Row(null, PaymentStatus.Pending, Now, PaymentEventKind.Transition);
        var second = Row(PaymentStatus.Pending, PaymentStatus.Success, Now.AddMinutes(1), PaymentEventKind.Transition);
        _queryService.Setup(q => q.PaymentExistsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _queryService.Setup(q => q.GetEventsAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);

        var result = await _events.Handle(new GetPaymentEventsQuery(id), CancellationToken.None);

        Assert.That(result.Value.Select(r => r.Id), Is.EqualTo(new[] { first.Id, second.Id }));
    }

    // ---- the replay command ----------------------------------------------------------------------------------

    private readonly ReplayFailedStripeWebhooksCommandValidator _replay = new();

    /// <summary>
    /// No body means "every outstanding capture". System.Text.Json writes an omitted collection as an explicit
    /// <c>null</c>, so the property is nullable and coalesced rather than initialised — the BUG-09 shape.
    /// </summary>
    [Test]
    public void ANullIdList_MeansEveryOutstandingCapture()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ReplayFailedStripeWebhooksCommand(null).EffectiveIds, Is.Empty);
            Assert.That(_replay.Validate(new ReplayFailedStripeWebhooksCommand(null)).IsValid, Is.True);
        });
    }

    [Test]
    public void AtMostTheCapMayBeNamed_AndMoreIsRefusedRatherThanTruncated()
    {
        var atTheCap = Enumerable.Range(0, ReplayFailedStripeWebhooksCommand.MaxReplayPerRequest)
            .Select(_ => Guid.NewGuid()).ToList();
        var overTheCap = atTheCap.Append(Guid.NewGuid()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(_replay.Validate(new ReplayFailedStripeWebhooksCommand(atTheCap)).IsValid, Is.True);
            Assert.That(_replay.Validate(new ReplayFailedStripeWebhooksCommand(overTheCap)).IsValid, Is.False);
        });
    }

    [Test]
    public void AnEmptyGuid_IsRefused()
    {
        var result = _replay.Validate(new ReplayFailedStripeWebhooksCommand([Guid.Empty]));

        Assert.That(result.IsValid, Is.False);
    }

    /// <summary>
    /// The command must not be transactional, and the reason is the opposite of a slip: a batch wraps many independent
    /// units of work, and inside one transaction the first failing row leaves Postgres refusing every later statement,
    /// so rows two onward would be reported as failures of a transaction rather than of themselves.
    /// </summary>
    [Test]
    public void TheReplayCommand_IsNotTransactional()
    {
        Assert.That(
            new ReplayFailedStripeWebhooksCommand(null),
            Is.Not.InstanceOf<EShop.BuildingBlocks.Application.Behaviors.ITransactionalCommand>());
    }
}
