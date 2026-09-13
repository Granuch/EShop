using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Infrastructure.Services;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Application.Extensions;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;
using EShop.Payment.Application.Payments.Refunds;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Consumers;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.IntegrationTests.Fixtures;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Payment audit D7, on PostgreSQL, through the real MediatR pipeline with <c>TransactionBehavior</c>. <c>/create-intent</c>
/// calls Stripe with no database transaction open, then records the intent in one save guarded by the payment's row
/// version. It used to run inside <c>TransactionBehavior</c>'s transaction. So each checkout held a pooled connection
/// across two Stripe round-trips, and a cancellation committing during the call left the new intent open at Stripe with
/// no record anywhere.
/// <para>Stripe is mocked. Each test acts inside the mocked intent call, which is exactly the window the design is
/// about.</para>
/// </summary>
[TestFixture]
public class CreatePaymentIntentWithoutTransactionTests
{
    private const string IntentId = "pi_outside_tx";

    private string _connectionString = null!;
    private ServiceProvider _services = null!;
    private Mock<IStripePaymentService> _stripe = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        _stripe = new Mock<IStripePaymentService>();
        var customers = new Mock<IStripeCustomerService>();
        customers.Setup(c => c.CreateOrGetCustomerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cus_outside_tx");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentApplication();
        services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(_connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentDbContext>());
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IIntegrationEventOutbox>(sp => new IntegrationEventOutbox(sp.GetRequiredService<PaymentDbContext>()));
        services.AddSingleton(_stripe.Object);
        services.AddSingleton(customers.Object);
        _services = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await _services.DisposeAsync();
        PostgresTestServer.ReleaseDatabase(_connectionString);
    }

    [Test]
    public async Task NoTransactionIsOpen_WhileStripeIsCalled()
    {
        var payment = await SeedPendingStripePaymentAsync();
        long? openTransactionsDuringTheCall = null;
        _stripe.Setup(s => s.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (StripePaymentIntentRequest _, CancellationToken _) =>
            {
                openTransactionsDuringTheCall = await OpenTransactionsAsync();
                return Intent();
            });

        var result = await SendAsync(payment.OrderId);

        var stored = await ReadAsync(payment.OrderId);
        Assert.Multiple(async () =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(openTransactionsDuringTheCall, Is.EqualTo(0), "no session may sit idle in a transaction during the Stripe call");
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(stored.PaymentIntentId, Is.EqualTo(IntentId));
            Assert.That(await PaymentCreatedEventsAsync(payment.OrderId), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The order is cancelled while Stripe creates the intent. The cancellation finds no intent, so only the request can
    /// cancel it. It used to lose its save and roll back, leaving the intent open at Stripe with nothing recording it.
    /// </summary>
    [Test]
    public async Task AnOrderCancelledDuringTheStripeCall_HasItsNewIntentCancelled_AndTheRequestIsAConflict()
    {
        var payment = await SeedPendingStripePaymentAsync();
        _stripe.Setup(s => s.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (StripePaymentIntentRequest _, CancellationToken _) =>
            {
                await CancelTheOrderAsync(payment.OrderId);
                return Intent();
            });
        _stripe.Setup(s => s.CancelPaymentIntentAsync(IntentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripePaymentIntentCancelResult(IntentId, "canceled"));

        var result = await SendAsync(payment.OrderId);

        var stored = await ReadAsync(payment.OrderId);
        Assert.Multiple(async () =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo("PAYMENT_ALREADY_EXISTS"));
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Cancelled), "the cancellation stands");
            Assert.That(stored.PaymentIntentId, Is.Empty);
            Assert.That(await PaymentCreatedEventsAsync(payment.OrderId), Is.Zero);
        });
        _stripe.Verify(s => s.CancelPaymentIntentAsync(IntentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A double submit. Both requests get the same intent from Stripe, because the idempotency key is the payment's.
    /// The second to save finds it already recorded and answers with it, instead of cancelling an intent the customer
    /// is about to pay.
    /// </summary>
    [Test]
    public async Task TwoRequestsAtOnce_BothGetTheSameIntent_RecordedOnce()
    {
        var payment = await SeedPendingStripePaymentAsync();
        var calls = 0;
        Result<CreatePaymentIntentDto>? second = null;
        _stripe.Setup(s => s.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (StripePaymentIntentRequest _, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    second = await SendAsync(payment.OrderId);
                }

                return Intent();
            });

        var first = await SendAsync(payment.OrderId);

        var stored = await ReadAsync(payment.OrderId);
        Assert.Multiple(async () =>
        {
            Assert.That(first.Value?.PaymentIntentId, Is.EqualTo(IntentId));
            Assert.That(second?.Value?.PaymentIntentId, Is.EqualTo(IntentId));
            Assert.That(stored.PaymentIntentId, Is.EqualTo(IntentId));
            Assert.That(await PaymentCreatedEventsAsync(payment.OrderId), Is.EqualTo(1));
        });
        _stripe.Verify(s => s.CancelPaymentIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static StripePaymentIntentResult Intent() => new(IntentId, "pi_outside_tx_secret", "requires_payment_method");

    private async Task<Result<CreatePaymentIntentDto>> SendAsync(Guid orderId)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new CreatePaymentIntentCommand(orderId, "user-1", false, null));
    }

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private async Task<PaymentTransaction> SeedPendingStripePaymentAsync()
    {
        await using var db = NewContext();
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            Amount = 40m,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = string.Empty,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private async Task<PaymentTransaction> ReadAsync(Guid orderId)
    {
        await using var db = NewContext();
        return await db.PaymentTransactions.AsNoTracking().SingleAsync(p => p.OrderId == orderId);
    }

    private async Task<int> PaymentCreatedEventsAsync(Guid orderId)
    {
        await using var db = NewContext();
        var order = orderId.ToString();
        return await db.Set<OutboxMessage>().CountAsync(m => m.Type.Contains(nameof(PaymentCreatedEvent)) && m.Payload.Contains(order));
    }

    /// <summary>Sessions on this test's database holding a transaction open while doing nothing.</summary>
    private async Task<long> OpenTransactionsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND state LIKE 'idle in transaction%'",
            connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>The real consumer on its own connection, as MassTransit would run it.</summary>
    private async Task CancelTheOrderAsync(Guid orderId)
    {
        await using var db = NewContext();
        var refunder = new PaymentRefunder(
            new PaymentRepository(db),
            new Mock<IPaymentProcessor>(MockBehavior.Strict).Object,
            _stripe.Object,
            new IntegrationEventOutbox(db),
            NullLogger<PaymentRefunder>.Instance);
        var consumer = new OrderCancelledConsumer(
            db,
            new PaymentRepository(db),
            db,
            _stripe.Object,
            refunder,
            Options.Create(new CancelledOrderRefundSettings()),
            NullLogger<OrderCancelledConsumer>.Instance);

        var context = new Mock<ConsumeContext<OrderCancelledEvent>>();
        context.SetupGet(x => x.Message).Returns(new OrderCancelledEvent { OrderId = orderId, UserId = "user-1", Reason = "changed my mind" });
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        await consumer.Consume(context.Object);
    }
}
