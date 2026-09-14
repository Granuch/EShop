using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EShop.Payment.Domain.Entities;
using EShop.Payment.IntegrationTests.Fixtures;

namespace EShop.Payment.IntegrationTests.Webhooks;

/// <summary>
/// Payment audit Stage 4 (H3, M10): <c>/webhooks/stripe</c> over HTTP, with signature checks on as compose and k8s
/// now ship them. Signatures are computed as Stripe computes them and checked by the real parser; nothing here
/// reaches Stripe. Before this stage no test posted to the webhook at all.
/// </summary>
[TestFixture]
[Category("Integration")]
public class StripeWebhookTests
{
    private StripeEnabledPaymentApiFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new StripeEnabledPaymentApiFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task ASignedSucceededEvent_SettlesItsPayment()
    {
        var (payment, intent) = await APaymentInFlightAsync();
        var payload = StripeWebhooks.Event("payment_intent.succeeded", intent, "succeeded");

        var response = await StripeWebhooks.PostAsync(_client, payload, StripeWebhooks.Sign(payload));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await StatusAsync(payment), Is.EqualTo(PaymentStatus.Success));
    }

    /// <summary>
    /// The endpoint is anonymous. Its answer used to carry <c>paymentFound</c> and <c>isDuplicate</c>, which told anyone
    /// whether an intent id belonged to a payment.
    /// </summary>
    [Test]
    public async Task TheAnswer_IsTheSame_WhetherTheIntentIsKnown_UnknownOrAlreadyDelivered()
    {
        var (_, intent) = await APaymentInFlightAsync();
        var known = StripeWebhooks.Event("payment_intent.succeeded", intent, "succeeded");
        var unknown = StripeWebhooks.Event("payment_intent.succeeded", StripeWebhooks.NewIntentId(), "succeeded");

        var answers = new List<(HttpStatusCode Status, string Body)>();
        foreach (var payload in new[] { known, unknown, known })
        {
            var response = await StripeWebhooks.PostAsync(_client, payload, StripeWebhooks.Sign(payload));
            answers.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        Assert.Multiple(() =>
        {
            Assert.That(answers.Select(a => a.Status), Is.All.EqualTo(HttpStatusCode.OK));
            Assert.That(answers.Select(a => a.Body).Distinct(), Has.Exactly(1).Items);
        });
    }

    [Test]
    public async Task AnEventSignedWithAnotherSecret_IsRefused_AndChangesNothing()
    {
        var (payment, intent) = await APaymentInFlightAsync();
        var payload = StripeWebhooks.Event("payment_intent.succeeded", intent, "succeeded");

        var response = await StripeWebhooks.PostAsync(_client, payload, StripeWebhooks.Sign(payload, "whsec_someone_else"));

        var problem = await StripeWebhooks.ProblemAsync(response);
        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(problem.ErrorCode, Is.EqualTo("STRIPE_WEBHOOK_INVALID"));
            // Our own wording, not Stripe.net's message (the root guide's "our strings yes, framework strings no").
            Assert.That(problem.Detail, Is.EqualTo("Stripe webhook signature does not match the payload."));
            Assert.That(await StatusAsync(payment), Is.EqualTo(PaymentStatus.Processing));
        });
    }

    [Test]
    public async Task APayloadChangedAfterSigning_IsRefused_AndChangesNothing()
    {
        var (payment, intent) = await APaymentInFlightAsync();
        var signed = StripeWebhooks.Event("payment_intent.succeeded", StripeWebhooks.NewIntentId(), "succeeded");
        var sent = StripeWebhooks.Event("payment_intent.succeeded", intent, "succeeded");

        var response = await StripeWebhooks.PostAsync(_client, sent, StripeWebhooks.Sign(signed));

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await StatusAsync(payment), Is.EqualTo(PaymentStatus.Processing));
        });
    }

    [Test]
    public async Task ADeliverySignedTenMinutesAgo_IsRefused_AndChangesNothing()
    {
        var (payment, intent) = await APaymentInFlightAsync();
        var payload = StripeWebhooks.Event("payment_intent.succeeded", intent, "succeeded");

        var response = await StripeWebhooks.PostAsync(
            _client, payload, StripeWebhooks.Sign(payload, signedAt: DateTimeOffset.UtcNow.AddMinutes(-10)));

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await StatusAsync(payment), Is.EqualTo(PaymentStatus.Processing));
        });
    }

    [Test]
    public async Task ADeliveryWithoutASignature_IsRefused()
    {
        var payload = StripeWebhooks.Event("payment_intent.succeeded", StripeWebhooks.NewIntentId(), "succeeded");

        var response = await StripeWebhooks.PostAsync(_client, payload, signature: null);

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await StripeWebhooks.ProblemAsync(response)).ErrorCode, Is.EqualTo("STRIPE_SIGNATURE_MISSING"));
        });
    }

    [TestCase("garbage")]
    [TestCase("t=not-a-number,v1=00")]
    [TestCase("t=1,v1=")]
    public async Task AMalformedSignatureHeader_IsRefused_NotAServerError(string header)
    {
        var payload = StripeWebhooks.Event("payment_intent.succeeded", StripeWebhooks.NewIntentId(), "succeeded");

        var response = await StripeWebhooks.PostAsync(_client, payload, header);

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await StripeWebhooks.ProblemAsync(response)).ErrorCode, Is.EqualTo("STRIPE_WEBHOOK_INVALID"));
        });
    }

    private async Task<(PaymentTransaction Payment, string IntentId)> APaymentInFlightAsync()
    {
        var intent = StripeWebhooks.NewIntentId();
        var payment = await _factory.SeedPaymentAsync("webhook-user", PaymentStatus.Processing, intentId: intent);
        return (payment, intent);
    }

    private async Task<PaymentStatus> StatusAsync(PaymentTransaction payment)
        => (await _factory.FindByOrderIdAsync(payment.OrderId))!.Status;
}

/// <summary>
/// Payment audit Stage 4 (M10), the Sandbox bypass: signatures are not checked, so a delivery's payload is the only
/// thing that can be wrong. A payload that failed to parse used to escape the endpoint's message-text match and be
/// answered 500, which Stripe redelivers for days.
/// </summary>
[TestFixture]
[Category("Integration")]
public class StripeWebhookBypassModeTests
{
    private StripeEnabledPaymentApiFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        _factory = new StripeEnabledPaymentApiFactory(verifyWebhookSignatures: false);
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestCase("not json")]
    [TestCase("{\"id\":")]
    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("null")]
    public async Task AMalformedPayload_IsRefused_NotAServerError(string payload)
    {
        var response = await StripeWebhooks.PostAsync(_client, payload, signature: null);

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await StripeWebhooks.ProblemAsync(response)).ErrorCode, Is.EqualTo("STRIPE_WEBHOOK_INVALID"));
        });
    }

    /// <summary>What the bypass is for, and why it is off everywhere but a developer's own machine.</summary>
    [Test]
    public async Task AnUnsignedEvent_IsAccepted()
    {
        var intent = StripeWebhooks.NewIntentId();
        var payment = await _factory.SeedPaymentAsync("webhook-user", PaymentStatus.Processing, intentId: intent);
        var payload = StripeWebhooks.Event("payment_intent.succeeded", intent, "succeeded");

        var response = await StripeWebhooks.PostAsync(_client, payload, signature: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await _factory.FindByOrderIdAsync(payment.OrderId))!.Status, Is.EqualTo(PaymentStatus.Success));
    }
}

/// <summary>Builds and signs webhook deliveries the way Stripe does.</summary>
internal static class StripeWebhooks
{
    public const string Endpoint = "/webhooks/stripe";

    public static string NewIntentId() => $"pi_test_{Guid.NewGuid():N}";

    /// <summary>
    /// A minimal Stripe event carrying a payment intent. <c>request</c> is not optional: Stripe.net's
    /// <c>EventConverter</c> throws <see cref="NullReferenceException"/> for an event without it, and every real event
    /// has one.
    /// </summary>
    public static string Event(string type, string intentId, string intentStatus) => JsonSerializer.Serialize(new
    {
        id = $"evt_test_{Guid.NewGuid():N}",
        @object = "event",
        type,
        api_version = "2024-06-20",
        created = 1_700_000_000,
        livemode = false,
        pending_webhooks = 1,
        request = new { id = (string?)null, idempotency_key = (string?)null },
        data = new
        {
            @object = new
            {
                id = intentId,
                @object = "payment_intent",
                status = intentStatus,
                metadata = new Dictionary<string, string>()
            }
        }
    });

    /// <summary>
    /// Stripe's scheme: <c>t={unix time},v1={hex HMAC-SHA256 of "{t}.{payload}" keyed by the webhook secret}</c>.
    /// </summary>
    public static string Sign(
        string payload,
        string secret = StripeEnabledPaymentApiFactory.WebhookSecret,
        DateTimeOffset? signedAt = null)
    {
        var timestamp = (signedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        }

        return client.SendAsync(request);
    }

    public static async Task<(string? ErrorCode, string? Detail)> ProblemAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        return (
            root.TryGetProperty("errorCode", out var code) ? code.GetString() : null,
            root.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }
}
