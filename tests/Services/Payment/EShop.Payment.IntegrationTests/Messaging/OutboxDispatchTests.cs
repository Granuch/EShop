using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Infrastructure.BackgroundServices;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.IntegrationTests.Fixtures;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EShop.Payment.IntegrationTests.Messaging;

/// <summary>
/// Ordering audit Stage 10. Payment wrote every integration event to <c>outbox_messages</c> and never
/// registered the processor that sends them, so nothing it published ever left the service — Ordering
/// never learned that a payment succeeded. Its unit tests could not notice: they assert that an event
/// was enqueued, on a mock. These tests assert that an event reaches the bus.
/// </summary>
[TestFixture]
[Category("Integration")]
public class OutboxDispatchTests : AuthenticatedIntegrationTestBase
{
    protected override PaymentApiFactory CreateFactory() => new BusCapturingPaymentApiFactory();

    [Test]
    public async Task APaymentMadeOverHttp_PublishesItsEvents_OnTheBus()
    {
        var harness = Factory.Services.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        var response = await Client.PostAsJsonAsync("/api/v1/payments", new
        {
            OrderId = orderId,
            UserId = TestUserId,
            Amount = 42.50m,
            Currency = "USD",
            PaymentMethod = "Mock"
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var successPublished = await harness.Published.Any<PaymentSuccessEvent>(
            m => m.Context.Message.OrderId == orderId);
        var createdPublished = await harness.Published.Any<PaymentCreatedEvent>(
            m => m.Context.Message.OrderId == orderId);

        Assert.Multiple(() =>
        {
            Assert.That(successPublished, Is.True,
                $"PaymentSuccessEvent was not published within {BusCapturingPaymentApiFactory.PublishTimeout}: "
                + "the outbox row was written but nothing dispatched it.");
            Assert.That(createdPublished, Is.True, "PaymentCreatedEvent was not published.");
        });
    }

    [Test]
    public void TheHost_RunsTheOutboxCleanup_AndReportsOutboxHealth()
    {
        var hostedServices = Factory.Services.GetServices<IHostedService>().Select(s => s.GetType()).ToList();
        var healthChecks = Factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToDictionary(r => r.Name);

        Assert.Multiple(() =>
        {
            Assert.That(hostedServices, Does.Contain(typeof(OutboxProcessorService)));
            Assert.That(hostedServices, Does.Contain(typeof(OutboxCleanupService)));
            Assert.That(healthChecks.ContainsKey("outbox"), Is.True, "no outbox health check registered");
            Assert.That(healthChecks["outbox"].Tags, Does.Contain("ready"));
        });
    }
}
