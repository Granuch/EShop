using System.Net;
using System.Text;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Services;
using Moq;
using Stripe;

namespace EShop.Payment.UnitTests.Services;

/// <summary>
/// Payment audit Stage 6 (H2, M1), against a fake Stripe HTTP endpoint. Our two creating calls carry idempotency keys,
/// so a rolled-back attempt's customer and intent come back on the retry. And a failure a retry can fix surfaces as
/// <see cref="PaymentProviderUnavailableException"/>, while one it cannot fix stays a <see cref="StripeException"/>.
/// <para>Payment audit Stage 9 (M4). Both services take their client by injection: here, a client over the fake, with
/// Stripe.net's own retries off. Stripe.net's process-wide client is pointed at a second fake, the trap. A call that falls
/// back to it lands there, and <see cref="RestoreStripe"/> fails the test. Until Stage 9 both services used that
/// process-wide client, and the key came from whichever <c>StripePaymentService</c> was built last.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class StripeProviderCallsTests
{
    private const string Key = "sk_test_unit_fake_stripe";
    private const string ProcessWideKey = "sk_test_process_wide_trap";

    private FakeStripe _stripe = null!;
    private FakeStripe _processWide = null!;
    private IStripeClient _client = null!;

    [SetUp]
    public void UseFakeStripe()
    {
        _stripe = new FakeStripe();
        _client = new StripeClient(
            apiKey: Key,
            httpClient: new SystemNetHttpClient(new HttpClient(_stripe), 0, null, false));

        _processWide = new FakeStripe();
        StripeConfiguration.ApiKey = ProcessWideKey;
        StripeConfiguration.StripeClient = new StripeClient(
            apiKey: ProcessWideKey,
            httpClient: new SystemNetHttpClient(new HttpClient(_processWide), 0, null, false));
    }

    [TearDown]
    public void RestoreStripe()
    {
        try
        {
            Assert.That(_processWide.Calls, Is.Empty, "a call went through Stripe.net's process-wide client");
        }
        finally
        {
            StripeConfiguration.StripeClient = null;
            StripeConfiguration.ApiKey = null;
            _stripe.Dispose();
            _processWide.Dispose();
        }
    }

    private StripePaymentService Payments() => new(_client);

    private StripeCustomerService Customers(Mock<IPaymentRepository> repository) => new(repository.Object, _client);

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
            Assert.That(keys[0], Is.EqualTo($"payment-intent-{paymentId}-1000"), "the payment and its amount in cents");
            Assert.That(keys[1], Is.EqualTo(keys[0]), "a retry for the same payment");
            Assert.That(keys[2], Is.Not.EqualTo(keys[0]), "another payment");
        });
    }

    /// <summary>
    /// frontend-contracts F-47. A payment's amount can change after an attempt to create its intent, and Stripe refuses
    /// a key reused with different parameters for 24 hours. Keyed by payment alone, the retry at the new amount would
    /// have been refused for a day.
    /// </summary>
    [Test]
    public async Task ANewAmount_GetsANewKey_SoStripeDoesNotRefuseTheRetry()
    {
        _stripe.Respond = _ => FakeStripe.Json(HttpStatusCode.OK,
            """{"id":"pi_1","object":"payment_intent","client_secret":"pi_1_secret_x","status":"requires_payment_method"}""");
        var paymentId = Guid.NewGuid();

        await Payments().CreatePaymentIntentAsync(IntentFor(paymentId) with { Amount = 591.99m });
        await Payments().CreatePaymentIntentAsync(IntentFor(paymentId) with { Amount = 675.99m });

        Assert.That(_stripe.Calls.Select(c => c.IdempotencyKey), Is.EqualTo(new[]
        {
            $"payment-intent-{paymentId}-59199",
            $"payment-intent-{paymentId}-67599"
        }));
    }

    /// <summary>
    /// frontend-contracts F-47. The order's new total goes to Stripe in cents, on the intent the customer already holds,
    /// under no key of ours: a later revision to another amount must not be refused as a reused key. (Stripe.net sends
    /// a random key per request, so two identical calls carry different keys.)
    /// </summary>
    [Test]
    public async Task AnAmountUpdate_PostsTheNewAmountInCents_ToTheSameIntent()
    {
        _stripe.Respond = _ => FakeStripe.Json(HttpStatusCode.OK,
            """{"id":"pi_1","object":"payment_intent","amount":67599,"status":"requires_payment_method"}""");

        var status = await Payments().UpdatePaymentIntentAmountAsync("pi_1", 675.99m, "USD");
        await Payments().UpdatePaymentIntentAmountAsync("pi_1", 675.99m, "USD");

        var call = _stripe.Calls[0];
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo("requires_payment_method"));
            Assert.That(call.Path, Is.EqualTo("/v1/payment_intents/pi_1"));
            Assert.That(call.Body, Does.Contain("amount=67599"));
            Assert.That(call.Body, Does.Contain("currency=usd"));
            Assert.That(call.IdempotencyKey, Does.Not.StartWith("payment-intent-"));
            Assert.That(_stripe.Calls[1].IdempotencyKey, Is.Not.EqualTo(call.IdempotencyKey), "no deterministic key of ours");
        });
    }

    /// <summary>
    /// frontend-contracts F-47. Stripe refusing the change because of the intent's state (it succeeded, is processing,
    /// or was cancelled) is deterministic, so it is a typed refusal the consumer dead-letters, not a retry.
    /// </summary>
    [Test]
    public void AnAmountUpdateStripeRefusesForTheIntentsState_IsNotUpdatable()
    {
        _stripe.Respond = _ => FakeStripe.Json(HttpStatusCode.BadRequest,
            """{"error":{"type":"invalid_request_error","code":"payment_intent_unexpected_state","message":"This PaymentIntent's amount could not be updated because it has a status of succeeded."}}""");

        var ex = Assert.ThrowsAsync<PaymentIntentNotUpdatableException>(
            () => Payments().UpdatePaymentIntentAmountAsync("pi_1", 675.99m, "USD"));

        Assert.That(ex!.StripeMessage, Does.Contain("status of succeeded"));
    }

    [TestCase(HttpStatusCode.ServiceUnavailable, "api_error")]
    [TestCase(HttpStatusCode.TooManyRequests, "rate_limit_error")]
    public void AnAmountUpdateARetryCanFix_IsProviderUnavailable(HttpStatusCode status, string type)
    {
        _stripe.Respond = _ => FakeStripe.Error(status, type);

        Assert.ThrowsAsync<PaymentProviderUnavailableException>(
            () => Payments().UpdatePaymentIntentAmountAsync("pi_1", 675.99m, "USD"));
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

    /// <summary>
    /// Payment audit Stage 9 (M4). Every operation both services perform reaches Stripe through the injected client, and
    /// none of them writes the process-wide key. The service's constructor used to set it.
    /// </summary>
    [Test]
    public async Task EveryStripeCall_GoesThroughTheInjectedClient_AndLeavesTheProcessWideKeyAlone()
    {
        _stripe.Respond = request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.StartsWith("/v1/refunds", StringComparison.Ordinal)
                => FakeStripe.Json(HttpStatusCode.OK, """{"id":"re_1","object":"refund","status":"succeeded"}"""),
            var path when path.StartsWith("/v1/customers", StringComparison.Ordinal)
                => FakeStripe.Json(HttpStatusCode.OK, """{"id":"cus_1","object":"customer"}"""),
            _ => FakeStripe.Json(HttpStatusCode.OK,
                """{"id":"pi_1","object":"payment_intent","client_secret":"pi_1_secret_x","status":"canceled"}""")
        };
        var payments = Payments();

        await payments.CreatePaymentIntentAsync(IntentFor(Guid.NewGuid()));
        await payments.CreateRefundAsync("pi_1", 10m, "USD");
        await payments.GetPaymentIntentStatusAsync("pi_1");
        await payments.CancelPaymentIntentAsync("pi_1");
        await payments.UpdatePaymentIntentAmountAsync("pi_1", 12m, "USD");
        await Customers(NoCustomerYet()).CreateOrGetCustomerAsync("user-1", null);

        Assert.Multiple(() =>
        {
            Assert.That(_stripe.Calls.Select(c => c.Path), Is.EqualTo(new[]
            {
                "/v1/payment_intents",
                "/v1/refunds",
                "/v1/payment_intents/pi_1",
                "/v1/payment_intents/pi_1",
                "/v1/payment_intents/pi_1/cancel",
                "/v1/payment_intents/pi_1",
                "/v1/customers"
            }));
            Assert.That(StripeConfiguration.ApiKey, Is.EqualTo(ProcessWideKey),
                "building and using the services must not write the process-wide key");
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
        public List<(string Path, string? IdempotencyKey, string Body)> Calls { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => Json(HttpStatusCode.OK, "{}");

        public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public static HttpResponseMessage Error(HttpStatusCode status, string type) =>
            Json(status, $$$"""{"error":{"type":"{{{type}}}","message":"fake Stripe says no"}}""");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.FirstOrDefault() : null;
            var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            Calls.Add((request.RequestUri!.AbsolutePath, key, body));
            return Task.FromResult(Respond(request));
        }
    }
}
