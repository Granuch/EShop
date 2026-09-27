using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.SystemAdmin;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Payment.IntegrationTests.Configuration;

/// <summary>
/// Admin panel S19: Payment's slices of the System page — the provider for the read-only settings (#85) and the
/// simulator and webhook bypass for the read-only feature flags (#89). The gateway composes them.
///
/// <para>
/// Both answers read configuration that sits beside the Stripe keys, so the Stripe host here is given distinctive key
/// values and every body is checked to contain none of them <b>by value</b> — a field-name check would pass a key
/// projected into an innocently named field (the S6 sessions lesson).
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class PaymentSystemTests
{
    private const string PublishableKey = "pk_test_s19_publishable_marker";

    private static readonly string[] Secrets =
        ["sk_test_not_a_real_key", StripeEnabledPaymentApiFactory.WebhookSecret, PublishableKey];

    private PaymentApiFactory _simulatorHost = null!;
    private StripeHost _stripeHost = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _simulatorHost = new PaymentApiFactory();
        _stripeHost = new StripeHost();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _simulatorHost.Dispose();
        _stripeHost.Dispose();
    }

    // ---------- settings ----------

    [Test]
    public async Task WithStripeOff_TheProviderIsTheSimulator()
        => Assert.That(await ReadAsync<PaymentSettingsDto>(_simulatorHost, SystemAdminPaths.Settings),
            Is.EqualTo(new PaymentSettingsDto(PaymentProviders.Simulator)));

    [Test]
    public async Task WithStripeOn_TheProviderIsStripe()
        => Assert.That(await ReadAsync<PaymentSettingsDto>(_stripeHost, SystemAdminPaths.Settings),
            Is.EqualTo(new PaymentSettingsDto(PaymentProviders.Stripe)));

    // ---------- feature flags ----------

    [Test]
    public async Task WithStripeOff_TheSimulatorIsActive_WithTheConfiguredValues()
    {
        // PaymentApiFactory.Settings: success 100 %, every delay 0, the default mode, no bypass.
        var flags = await ReadAsync<PaymentFeatureFlagsDto>(_simulatorHost, SystemAdminPaths.FeatureFlags);

        Assert.That(flags, Is.EqualTo(new PaymentFeatureFlagsDto(
            SimulatorActive: true,
            SimulationMode: "Random",
            SuccessRatePercent: 100,
            ProcessingDelayMinSeconds: 0,
            ProcessingDelayMaxSeconds: 0,
            RefundDelaySeconds: 0,
            WebhookSignatureVerificationSkipped: false)));
    }

    [Test]
    public async Task WithStripeOn_TheSimulatorIsInactive_ButItsValuesAndTheBypassAreReported()
    {
        var flags = await ReadAsync<PaymentFeatureFlagsDto>(_stripeHost, SystemAdminPaths.FeatureFlags);

        Assert.Multiple(() =>
        {
            Assert.That(flags!.SimulatorActive, Is.False);
            Assert.That(flags.SimulationMode, Is.EqualTo("AlwaysFailure"));
            Assert.That(flags.SuccessRatePercent, Is.EqualTo(37));
            Assert.That(flags.WebhookSignatureVerificationSkipped, Is.True);
        });
    }

    [TestCase(SystemAdminPaths.Settings)]
    [TestCase(SystemAdminPaths.FeatureFlags)]
    public async Task NoStripeKey_IsEverInTheAnswer(string path)
    {
        using var response = await SendAsync(_stripeHost, path, Token("Admin"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        foreach (var secret in Secrets)
        {
            Assert.That(body, Does.Not.Contain(secret));
        }
    }

    // ---------- authorization ----------

    [TestCase(SystemAdminPaths.Settings)]
    [TestCase(SystemAdminPaths.FeatureFlags)]
    public async Task TheSystemManagePermission_IsEnough_WithoutTheAdminRole(string path)
    {
        using var response = await SendAsync(_simulatorHost, path, Token(role: null, EShopPermissions.SystemManage));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase(SystemAdminPaths.Settings)]
    [TestCase(SystemAdminPaths.FeatureFlags)]
    public async Task APaymentsPermission_IsNot(string path)
    {
        using var response = await SendAsync(_simulatorHost, path, Token(role: null, EShopPermissions.PaymentsRead));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [TestCase(SystemAdminPaths.Settings)]
    [TestCase(SystemAdminPaths.FeatureFlags)]
    public async Task ACustomer_IsForbidden(string path)
    {
        using var response = await SendAsync(_simulatorHost, path, Token("User"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [TestCase(SystemAdminPaths.Settings)]
    [TestCase(SystemAdminPaths.FeatureFlags)]
    public async Task AnAnonymousCaller_IsUnauthorized(string path)
    {
        using var response = await SendAsync(_simulatorHost, path, token: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // ---------- helpers ----------

    private static async Task<T?> ReadAsync<T>(PaymentApiFactory host, string path)
    {
        using var response = await SendAsync(host, path, Token("Admin"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private static async Task<HttpResponseMessage> SendAsync(PaymentApiFactory host, string path, string? token)
    {
        // A fresh client per call: nothing carries a default Authorization header, so an anonymous call really is one.
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    private static string Token(string? role, string? permission = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "operator-1"),
            new(JwtRegisteredClaimNames.Sub, "operator-1"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        if (permission is not null) claims.Add(new Claim(EShopPermissions.ClaimType, permission));

        var token = new JwtSecurityToken(
            issuer: PaymentApiFactory.TestJwtIssuer,
            audience: PaymentApiFactory.TestJwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PaymentApiFactory.TestJwtSecretKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Stripe on, the webhook bypass on, a publishable key, and simulator values unlike the base host's.</summary>
    private sealed class StripeHost() : StripeEnabledPaymentApiFactory(verifyWebhookSignatures: false)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Last UseSetting for a key wins, so these replace the base factory's simulation values.
            builder.UseSetting("Stripe:PublishableKey", PublishableKey);
            builder.UseSetting("PaymentSimulation:Mode", "AlwaysFailure");
            builder.UseSetting("PaymentSimulation:SuccessRatePercent", "37");
        }
    }
}
