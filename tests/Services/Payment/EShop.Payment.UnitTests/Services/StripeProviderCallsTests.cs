using System.Net;
using System.Text;
using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Moq;
using Stripe;

namespace EShop.Payment.UnitTests.Services;

/// <summary>
/// Payment audit Stage 6 (H2, M1), against a fake Stripe HTTP endpoint. Our two creating calls carry idempotency keys,
/// so a rolled-back attempt's customer and intent come back on the retry. And a failure a retry can fix surfaces as
/// <see cref="PaymentProviderUnavailableException"/>, while one it cannot fix stays a <see cref="StripeException"/>.
/// <para>Both services use Stripe.net's process-wide client, so this fixture swaps in a client over a fake handler,
/// with Stripe.net's own retries off. Setting <c>StripeConfiguration.ApiKey</c> to the same value keeps that client.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class StripeProviderCallsTests
{
    private const string Key = "sk_test_unit_fake_stripe";

    private FakeStripe _stripe = null!;

    [SetUp]
    public void UseFakeStripe()
    {
        _stripe = new FakeStripe();
        StripeConfiguration.ApiKey = Key;
        StripeConfiguration.StripeClient = new StripeClient(
            apiKey: Key,
            httpClient: new SystemNetHttpClient(new HttpClient(_stripe), 0, null, false));
    }

    [TearDown]
    public void RestoreStripe()
    {
        StripeConfiguration.StripeClient = null;
        _stripe.Dispose();
    }

    private static StripePaymentService Payments() => new(Options.Create(new StripeSettings { SecretKey = Key }));

    private static StripeCustomerService Customers(Mock<IPaymentRepository> repository) => new(repository.Object);

    private static Mock<IPaymentRepository> NoCustomerYet()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(r => r.GetCustomerByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentCustomer?)null);
        repository.Setup(r => r.AddCustomerIfAbsentAsync(It.IsAny<PaymentCustomer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentCustomer c, CancellationToken _) => c);
        return repository;
    }

    private static StripePaymentIntentRequest IntentFor(Guid paymentId)
        => new(paymentId, Guid.NewGuid(), "user-1", "cus_1", 10m, "USD");

    [Test]
    public async Task AnIntent_IsCreatedUnderItsPaymentsKey_TheSameOnEveryAttempt()
    {
        _stripe.Respond = _ => FakeStripe.Json(HttpStatusCode.OK,
            """{"id":"pi_1","object":"payment_intent","client_secret":"pi_1_secret_x","status":"requires_payment_method"}""");
        var paymentId = Guid.NewGuid();

        await Payments().CreatePaymentIntentAsync(IntentFor(paymentId));
        await Payments().CreatePaymentIntentAsync(IntentFor(paymentId));
        await Payments().CreatePaymentIntentAsync(IntentFor(Guid.NewGuid()));

        var keys = _stripe.Calls.Select(c => c.IdempotencyKey).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(keys[0], Is.EqualTo($"payment-intent-{paymentId}"));
            Assert.That(keys[1], Is.EqualTo(keys[0]), "a retry for the same payment");
            Assert.That(keys[2], Is.Not.EqualTo(keys[0]), "another payment");
        });
    }

    [Test]
    public async Task ACustomer_IsCreatedUnderAKeyForTheUserAndEmail()
    {
        _stripe.Respond = _ => FakeStripe.Json(HttpStatusCode.OK, """{"id":"cus_1","object":"customer"}""");

        await Customers(NoCustomerYet()).CreateOrGetCustomerAsync("user-1", "a@example.test");
        await Customers(NoCustomerYet()).CreateOrGetCustomerAsync("user-1", "a@example.test");
        await Customers(NoCustomerYet()).CreateOrGetCustomerAsync("user-1", "b@example.test");

        var keys = _stripe.Calls.Select(c => c.IdempotencyKey).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(keys[0], Does.StartWith("customer-user-1-"));
            Assert.That(keys[1], Is.EqualTo(keys[0]), "a retry for the same user");
            Assert.That(keys[2], Is.Not.EqualTo(keys[0]), "Stripe refuses one key with different parameters");
        });
    }

    [TestCase(HttpStatusCode.ServiceUnavailable, "api_error")]
    [TestCase(HttpStatusCode.InternalServerError, "api_error")]
    [TestCase(HttpStatusCode.TooManyRequests, "rate_limit_error")]
    [TestCase(HttpStatusCode.Conflict, "idempotency_error")]
    public void AFailureARetryCanFix_IsProviderUnavailable(HttpStatusCode status, string type)
    {
        _stripe.Respond = _ => FakeStripe.Error(status, type);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => Payments().CreatePaymentIntentAsync(IntentFor(Guid.NewGuid())));
            Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => Customers(NoCustomerYet()).CreateOrGetCustomerAsync("user-1", null));
        });
    }

    [Test]
    public void TheNetworkFailing_IsProviderUnavailable()
    {
        _stripe.Respond = _ => throw new HttpRequestException("No route to api.stripe.com");

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => Payments().CreatePaymentIntentAsync(IntentFor(Guid.NewGuid())));
            Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => Customers(NoCustomerYet()).CreateOrGetCustomerAsync("user-1", null));
        });
    }

    /// <summary>A bad request or a bad key: our defect or our configuration, which no retry fixes. It stays a 500.</summary>
    [TestCase(HttpStatusCode.BadRequest, "invalid_request_error")]
    [TestCase(HttpStatusCode.Unauthorized, "invalid_request_error")]
    public void AFailureARetryCannotFix_StaysAStripeError(HttpStatusCode status, string type)
    {
        _stripe.Respond = _ => FakeStripe.Error(status, type);

        var ex = Assert.ThrowsAsync<StripeException>(() => Payments().CreatePaymentIntentAsync(IntentFor(Guid.NewGuid())));

        Assert.That(ex!.HttpStatusCode, Is.EqualTo(status));
    }

    [Test]
    public void ACustomerStripeCouldNotCreate_IsNeverMapped()
    {
        _stripe.Respond = _ => FakeStripe.Error(HttpStatusCode.ServiceUnavailable, "api_error");
        var repository = NoCustomerYet();

        Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => Customers(repository).CreateOrGetCustomerAsync("user-1", null));

        repository.Verify(r => r.AddCustomerIfAbsentAsync(It.IsAny<PaymentCustomer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Records each request's path and idempotency key, and answers with <see cref="Respond"/>.</summary>
    private sealed class FakeStripe : HttpMessageHandler
    {
        public List<(string Path, string? IdempotencyKey)> Calls { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => Json(HttpStatusCode.OK, "{}");

        public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public static HttpResponseMessage Error(HttpStatusCode status, string type) =>
            Json(status, $$$"""{"error":{"type":"{{{type}}}","message":"fake Stripe says no"}}""");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.FirstOrDefault() : null;
            Calls.Add((request.RequestUri!.AbsolutePath, key));
            return Task.FromResult(Respond(request));
        }
    }
}
