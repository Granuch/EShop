using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.QueryServices;
using EShop.Payment.Infrastructure.Services;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Admin panel S11 (M7/M8), on real PostgreSQL.
///
/// <para>
/// Payment's HTTP suite runs on EF InMemory, which has no transactions, no aborted-transaction state and no filtered
/// unique indexes. Two of this stage's claims are about exactly those, so neither is checkable there:
/// <list type="bullet">
///   <item><b>the capture survives a failing unit of work</b> — on InMemory there is no transaction to be poisoned,
///   so a capture written through the failing context would happily succeed and the test would pass on the broken
///   version;</item>
///   <item><b>one row per Stripe event</b> — enforced by a unique index the InMemory provider does not create.</item>
/// </list>
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentDiagnosticsSqlTests
{
    private static readonly DateTime Jan = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private string _connectionString = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task CreateDatabaseAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddDbContext<PaymentDbContext>(o => o.UseNpgsql(_connectionString));
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task ReleaseDatabaseAsync()
    {
        await _provider.DisposeAsync();
        PostgresTestServer.ReleaseDatabase(_connectionString);
    }

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private static PaymentTransaction APayment()
        => PaymentTransaction.RecordForOrder(Guid.NewGuid(), "customer-1", 40m, PaymentMethodType.Stripe, Jan);

    private static string APayload(string eventId)
        => $"{{\"id\":\"{eventId}\",\"type\":\"payment_intent.succeeded\"}}";

    private FailedStripeWebhookStore NewStore()
        => new(_provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FailedStripeWebhookStore>.Instance);

    // ---- the timeline ----------------------------------------------------------------------------------------

    /// <summary>
    /// The aggregate appends rows to a payment it has already saved once — the <c>ValueGeneratedNever()</c> case —
    /// and they round-trip through Npgsql with their enums stored as names.
    /// </summary>
    [Test]
    public async Task TheTimeline_RoundTripsOnPostgres_OldestFirst()
    {
        var payment = APayment();
        await using (var db = NewContext())
        {
            db.PaymentTransactions.Add(payment);
            await db.SaveChangesAsync();

            payment.StartStripePayment("pi_1", "cus_1", "requires_payment_method", Jan.AddMinutes(1));
            await db.SaveChangesAsync();

            payment.RecordStripeSuccess("succeeded", Jan.AddMinutes(2), "evt_1");
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var rows = await new PaymentQueryService(read).GetEventsAsync(payment.Id);

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.ToStatus), Is.EqualTo(new[]
            {
                PaymentStatus.Pending, PaymentStatus.Processing, PaymentStatus.Success
            }));
            Assert.That(rows[0].FromStatus, Is.Null);
            Assert.That(rows[2].Kind, Is.EqualTo(PaymentEventKind.Webhook));
            Assert.That(rows[2].StripeEventId, Is.EqualTo("evt_1"));
        });
    }

    /// <summary>The enums must be readable strings in the table, not ordinals that shift when the enum is reordered.</summary>
    [Test]
    public async Task TheEnumsAreStoredAsNames()
    {
        var payment = APayment();
        await using (var db = NewContext())
        {
            db.PaymentTransactions.Add(payment);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var stored = await read.Database
            .SqlQueryRaw<string>("""SELECT "Kind" || '/' || "ToStatus" AS "Value" FROM "PaymentEvents" """)
            .ToListAsync();

        Assert.That(stored, Is.EqualTo(new[] { "Transition/Pending" }));
    }

    [Test]
    public async Task DeletingAPayment_TakesItsTimelineWithIt()
    {
        var payment = APayment();
        await using (var db = NewContext())
        {
            db.PaymentTransactions.Add(payment);
            await db.SaveChangesAsync();
            db.PaymentTransactions.Remove(payment);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        Assert.That(await read.PaymentEvents.CountAsync(), Is.Zero);
    }

    // ---- the capture -----------------------------------------------------------------------------------------

    /// <summary>
    /// The plan's named risk, shown rather than asserted. A real aborted transaction is staged on one context; the
    /// capture then has to land anyway, which it can only do through a connection of its own.
    ///
    /// <para>Note what this does NOT need: the webhook endpoint. The property is about the store, and staging a
    /// genuinely poisoned Postgres transaction is far more direct than provoking one through HTTP.</para>
    /// </summary>
    [Test]
    public async Task ACapture_SurvivesAnAbortedTransaction()
    {
        await using var failing = NewContext();
        await using var transaction = await failing.Database.BeginTransactionAsync();

        // Poison it: a duplicate primary key inside the transaction, so Postgres refuses every later statement on
        // this connection with 25P02.
        var payment = APayment();
        failing.PaymentTransactions.Add(payment);
        await failing.SaveChangesAsync();
        Assert.CatchAsync(async () => await failing.Database.ExecuteSqlRawAsync(
            $"""INSERT INTO "PaymentTransactions" ("Id","OrderId","UserId","Amount","Currency","PaymentMethod","PaymentIntentId","Status","CreatedAt") VALUES ('{payment.Id}','{Guid.NewGuid()}','x',1,'USD','Mock','','Pending',now())"""));

        // From here every statement on that connection answers 25P02 until the transaction ends. Proving that is the
        // point, so it is asserted rather than assumed.
        Assert.CatchAsync(async () => await failing.PaymentTransactions.CountAsync());

        await NewStore().CaptureAsync(APayload("evt_aborted"), "t=1,v1=x", new InvalidOperationException("boom"));

        await transaction.RollbackAsync();

        await using var read = NewContext();
        Assert.Multiple(async () =>
        {
            Assert.That(await read.FailedStripeWebhooks.CountAsync(w => w.StripeEventId == "evt_aborted"),
                Is.EqualTo(1), "the capture must not be written through the context that just failed");
            Assert.That(await read.PaymentTransactions.CountAsync(), Is.Zero,
                "and it must not drag the rolled-back work along with it");
        });
    }

    /// <summary>One row per Stripe event, enforced by the filtered unique index rather than by the read-then-write.</summary>
    [Test]
    public async Task TwoCapturesOfOneEvent_CannotBothBeStored()
    {
        await using var db = NewContext();
        db.FailedStripeWebhooks.Add(FailedStripeWebhook.Capture(
            "evt_dup", "payment_intent.succeeded", "{}", "sig", "first", DateTime.UtcNow));
        db.FailedStripeWebhooks.Add(FailedStripeWebhook.Capture(
            "evt_dup", "payment_intent.succeeded", "{}", "sig", "second", DateTime.UtcNow));

        Assert.CatchAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    /// <summary>
    /// The index is filtered, so payloads that arrived without an event id — which the parser should have refused, so
    /// each one is itself a finding — are captured separately rather than collapsed onto one another.
    /// </summary>
    [Test]
    public async Task TwoCapturesWithNoEventId_AreBothStored()
    {
        await using var db = NewContext();
        db.FailedStripeWebhooks.Add(FailedStripeWebhook.Capture(null, null, "{}", "sig", "first", DateTime.UtcNow));
        db.FailedStripeWebhooks.Add(FailedStripeWebhook.Capture(null, null, "{}", "sig", "second", DateTime.UtcNow));

        await db.SaveChangesAsync();

        Assert.That(await db.FailedStripeWebhooks.CountAsync(), Is.EqualTo(2));
    }

    /// <summary>A capture never throws, so a webhook that failed still answers 500 rather than a different 500.</summary>
    [Test]
    public async Task ACaptureThatCannotBeWritten_DoesNotThrow()
    {
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("""DROP TABLE "FailedStripeWebhooks" """);
        }

        Assert.DoesNotThrowAsync(async () =>
            await NewStore().CaptureAsync(APayload("evt_gone"), "sig", new InvalidOperationException("boom")));
    }

    [Test]
    public async Task TheIndexesTheReplayAndTheTimelineNeed_Exist()
    {
        await using var db = NewContext();

        var indexes = await db.Database
            .SqlQueryRaw<string>("""
                SELECT indexdef AS "Value" FROM pg_indexes
                WHERE tablename IN ('PaymentEvents', 'FailedStripeWebhooks')
                """)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(indexes.Any(i => i.Contains("(\"PaymentTransactionId\", \"OccurredAt\")")), Is.True,
                "the timeline's only query: " + string.Join(" | ", indexes));
            Assert.That(
                indexes.Any(i => i.Contains("UNIQUE") && i.Contains("\"StripeEventId\" IS NOT NULL")), Is.True,
                "one capture per Stripe event: " + string.Join(" | ", indexes));
            Assert.That(indexes.Any(i => i.Contains("\"ReplayedAt\" IS NULL")), Is.True,
                "the replay's only query: " + string.Join(" | ", indexes));
        });
    }
}
