using EShop.Notification.API.Configuration;
using EShop.Notification.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EShop.Notification.IntegrationTests.Configuration;

/// <summary>
/// Notification audit S4 (M9, M10, L6, L23; D4): the guard's rules, one by one, without a host. That <c>Program.cs</c>
/// actually calls it is <c>StartupGuardTests</c>' job.
/// </summary>
[TestFixture]
public class NotificationConfigurationGuardTests
{
    private static Dictionary<string, string?> Clean() => new()
    {
        ["ConnectionStrings:NotificationDb"] = "Host=notification-postgres;Database=eshop_notification",
        ["IdentityService:BaseUrl"] = "http://identity-api:8080",
        ["IdentityService:ApiKey"] = "k7Qp2vXw9sLm4tRz8bNc6yHd3fJg5aUe1oWi0",
        ["PasswordReset:ResetUrlBase"] = "https://shop.eshop-real.test/reset-password",
        ["Smtp:Host"] = "smtp.eshop-real.test",
        ["Smtp:FromEmail"] = "noreply@eshop.local",
        ["RabbitMQ:Host"] = "rabbitmq",
        ["RabbitMQ:Username"] = "eshop",
        ["RabbitMQ:Password"] = "s3cret-rabbit-password",
        // Admin panel S12: the guard now also gates the web surface's token settings.
        ["JwtSettings:SecretKey"] = "NotificationSuiteSigningMaterialLongEnoughForHS256!",
        ["JwtSettings:Issuer"] = "EShop.Identity",
        ["JwtSettings:Audience"] = "EShop.Services"
    };

    [TestCase("Production")]
    [TestCase("Sandbox")]
    [TestCase("Development")]
    [TestCase("Testing")]
    public void ACleanConfiguration_Passes(string environment)
        => Assert.DoesNotThrow(() => Validate(environment, Clean()));

    /// <summary>Checked as Testing, the most lenient environment, to show the rule holds everywhere.</summary>
    [TestCase("IdentityService:BaseUrl", "", "IdentityService:BaseUrl is required")]
    [TestCase("IdentityService:BaseUrl", "identity-api:8080", "IdentityService:BaseUrl must be an absolute http or https URL")]
    [TestCase("PasswordReset:ResetUrlBase", "", "PasswordReset:ResetUrlBase is required")]
    [TestCase("PasswordReset:ResetUrlBase", "/reset-password", "PasswordReset:ResetUrlBase must be an absolute http or https URL")]
    [TestCase("PasswordReset:ResetUrlBase", "ftp://shop.eshop-real.test/reset", "PasswordReset:ResetUrlBase must be an absolute http or https URL")]
    [TestCase("Smtp:Host", "", "Smtp:Host is required")]
    [TestCase("Smtp:FromEmail", "", "Smtp:FromEmail is required")]
    [TestCase("Smtp:FromEmail", "not-an-address", "Smtp:FromEmail must be an email address")]
    public void ASettingEveryEnvironmentNeeds_IsRefusedWhenMissingOrMalformed(string key, string value, string message)
    {
        var settings = Clean();
        settings[key] = value;

        var thrown = Assert.Throws<InvalidOperationException>(() => Validate("Testing", settings));
        Assert.That(thrown!.Message, Does.Contain(message));
    }

    [Test]
    public void TheConnectionString_IsRequiredOutsideTesting_WhichRunsInMemory()
    {
        var settings = Clean();
        settings["ConnectionStrings:NotificationDb"] = "";

        Assert.Multiple(() =>
        {
            Assert.That(() => Validate("Production", settings),
                Throws.InvalidOperationException.With.Message.Contains("ConnectionStrings:NotificationDb is required"));
            Assert.That(() => Validate("Testing", settings), Throws.Nothing);
        });
    }

    [TestCase("Production")]
    [TestCase("Sandbox")]
    public void TheIdentityApiKey_IsRequiredWhereverServicesAreDeployed(string environment)
    {
        var settings = Clean();
        settings["IdentityService:ApiKey"] = "";

        Assert.That(() => Validate(environment, settings),
            Throws.InvalidOperationException.With.Message.Contains("IdentityService:ApiKey is required"));
    }

    /// <summary>
    /// The first is what the tracked appsettings.Development.json ships; together the cases cover all seven of the shared
    /// patterns. A <c>#{…}#</c> token in a URL setting never gets this far: it does not parse as a URL, so the URL rule
    /// refuses it first.
    /// </summary>
    [TestCase("IdentityService:ApiKey", "LOCAL_internal_service_api_key")]
    [TestCase("IdentityService:ApiKey", "CHANGE_ME_internal_service_api_key")]
    [TestCase("IdentityService:BaseUrl", "http://TestKey-identity:8080")]
    [TestCase("PasswordReset:ResetUrlBase", "https://YOUR_STOREFRONT/reset-password")]
    [TestCase("RabbitMQ:Host", "REPLACE_WITH_rabbitmq_host")]
    [TestCase("RabbitMQ:Username", "placeholder-user")]
    [TestCase("RabbitMQ:Password", "#{RABBITMQ_PASSWORD}#")]
    [TestCase("Smtp:Username", "CHANGE_ME_smtp_username")]
    [TestCase("Smtp:Password", "CHANGE_ME_smtp_password")]
    public void APlaceholder_IsRefusedInSandbox_ButAllowedInDevelopment(string key, string value)
    {
        var settings = Clean();
        settings[key] = value;

        Assert.Multiple(() =>
        {
            Assert.That(() => Validate("Sandbox", settings),
                Throws.InvalidOperationException.With.Message.Contains($"{key} contains placeholder pattern"));
            Assert.That(() => Validate("Development", settings), Throws.Nothing);
        });
    }

    /// <summary>D4. The first is the old tracked default, which reached every deployed environment.</summary>
    [TestCase("https://localhost:3000/reset-password", "points at localhost")]
    [TestCase("https://127.0.0.1/reset-password", "points at 127.0.0.1")]
    [TestCase("http://shop.eshop-real.test/reset-password", "must use HTTPS in Production")]
    public void InProduction_TheResetUrl_MustBeThePublicHttpsStorefront(string url, string message)
    {
        var settings = Clean();
        settings["PasswordReset:ResetUrlBase"] = url;

        Assert.That(() => Validate("Production", settings),
            Throws.InvalidOperationException.With.Message.Contains(message));
    }

    /// <summary>D4. The compose and k8s default: the local stack's browser reaches the storefront on localhost:3000.</summary>
    [Test]
    public void InSandbox_TheLocalStorefrontUrl_IsAccepted()
    {
        var settings = Clean();
        settings["PasswordReset:ResetUrlBase"] = "http://localhost:3000/reset-password";

        Assert.DoesNotThrow(() => Validate("Sandbox", settings));
    }

    /// <summary>S5 (M6, D7). The second is the old compose default, UseSsl=false, with credentials added.</summary>
    [TestCase("Sandbox", "Smtp:Security", "None")]
    [TestCase("Sandbox", "Smtp:UseSsl", "false")]
    [TestCase("Production", "Smtp:Security", "None")]
    public void CredentialsOverAPlaintextConnection_AreRefused(string environment, string modeKey, string modeValue)
    {
        var settings = Clean();
        settings[modeKey] = modeValue;
        settings["Smtp:Username"] = "mailer";
        settings["Smtp:Password"] = "s3cret-smtp-password";

        Assert.That(() => Validate(environment, settings),
            Throws.InvalidOperationException.With.Message.Contains("would travel in cleartext"));
    }

    /// <summary>D7. The compose and k8s default: Mailpit, plaintext, no credentials.</summary>
    [Test]
    public void InSandbox_MailpitWithoutTlsOrCredentials_IsAccepted()
    {
        var settings = Clean();
        settings["Smtp:Security"] = "None";

        Assert.DoesNotThrow(() => Validate("Sandbox", settings));
    }

    [Test]
    public void InDevelopment_CredentialsOverPlaintext_AreAllowed()
    {
        var settings = Clean();
        settings["Smtp:Security"] = "None";
        settings["Smtp:Username"] = "mailer";

        Assert.DoesNotThrow(() => Validate("Development", settings));
    }

    [Test]
    public void InProduction_ThePlaintextMode_IsRefused_EvenWithoutCredentials()
    {
        var settings = Clean();
        settings["Smtp:Security"] = "None";

        Assert.That(() => Validate("Production", settings),
            Throws.InvalidOperationException.With.Message.Contains("Smtp:Security must be StartTls or SslOnConnect"));
    }

    [TestCase("StartTls")]
    [TestCase("SslOnConnect")]
    public void InProduction_AnEncryptedConnectionWithCredentials_IsAccepted(string mode)
    {
        var settings = Clean();
        settings["Smtp:Security"] = mode;
        settings["Smtp:Username"] = "mailer";
        settings["Smtp:Password"] = "s3cret-smtp-password";

        Assert.DoesNotThrow(() => Validate("Production", settings));
    }

    [Test]
    public void AnUnknownSecurityMode_IsRefused_Everywhere()
    {
        var settings = Clean();
        settings["Smtp:Security"] = "Tls";

        Assert.That(() => Validate("Testing", settings),
            Throws.InvalidOperationException.With.Message.Contains("Smtp:Security"));
    }

    // ---------- Admin panel S12: the token settings ----------

    /// <summary>
    /// The key is checked in <b>every</b> environment, Testing included — HS256 needs 256 bits whoever is asking, and a
    /// host that cannot validate a token has no usable web surface. That is <c>JwtSecretGuard</c>'s rule, not a local one.
    /// </summary>
    [TestCase("", "JWT SecretKey is not configured")]
    [TestCase("too-short", "at least 32 characters")]
    public void TheSigningKey_IsRefusedEverywhere_WhenMissingOrTooShort(string key, string message)
    {
        var settings = Clean();
        settings["JwtSettings:SecretKey"] = key;

        Assert.Multiple(() =>
        {
            foreach (var environment in new[] { "Testing", "Development", "Sandbox", "Production" })
            {
                Assert.That(() => Validate(environment, settings),
                    Throws.InvalidOperationException.With.Message.Contains(message), environment);
            }
        });
    }

    /// <summary>The first is what the tracked appsettings.json ships, the second what appsettings.Development.json does.</summary>
    [TestCase("CHANGE_ME_notification_service_secret_key_32_chars_min")]
    [TestCase("LOCAL_notification_service_dev_secret_key_32_chars_min")]
    public void APlaceholderSigningKey_IsRefusedInSandbox_ButAllowedInDevelopment(string key)
    {
        var settings = Clean();
        settings["JwtSettings:SecretKey"] = key;

        Assert.Multiple(() =>
        {
            Assert.That(() => Validate("Sandbox", settings),
                Throws.InvalidOperationException.With.Message.Contains("placeholder"));
            Assert.That(() => Validate("Development", settings), Throws.Nothing);
        });
    }

    /// <summary>
    /// Program.cs sets ValidateIssuer and ValidateAudience, so an empty one is not a lenient default: it is a service
    /// that rejects every token it is ever shown while reporting healthy. Refused everywhere for that reason.
    /// </summary>
    [TestCase("JwtSettings:Issuer", "JwtSettings:Issuer is required")]
    [TestCase("JwtSettings:Audience", "JwtSettings:Audience is required")]
    public void TheIssuerAndAudience_AreRequiredEverywhere(string key, string message)
    {
        var settings = Clean();
        settings[key] = "";

        Assert.Multiple(() =>
        {
            Assert.That(() => Validate("Testing", settings),
                Throws.InvalidOperationException.With.Message.Contains(message));
            Assert.That(() => Validate("Production", settings),
                Throws.InvalidOperationException.With.Message.Contains(message));
        });
    }

    /// <summary>L23. A content root that is the repository makes the renderer read the source tree; one file is gone.</summary>
    [Test]
    public void AMissingTemplate_IsRefused_NamingIt()
    {
        var contentRoot = Directory.CreateTempSubdirectory("notification-guard-").FullName;
        try
        {
            var templates = Path.Combine(
                contentRoot, "src", "Services", "Notification", "EShop.Notification.Infrastructure", "Templates");
            Directory.CreateDirectory(templates);
            foreach (var name in NotificationTemplates.All.Where(n => n != NotificationTemplates.PaymentRefunded))
            {
                File.WriteAllText(Path.Combine(templates, $"{name}.html"), "<p>{{CustomerName}}</p>");
            }

            Assert.That(() => Validate("Testing", Clean(), contentRoot),
                Throws.InvalidOperationException.With.Message.Contains("Email templates are missing")
                    .And.Message.Contains(NotificationTemplates.PaymentRefunded));

            File.WriteAllText(Path.Combine(templates, $"{NotificationTemplates.PaymentRefunded}.html"), "<p></p>");
            Assert.That(() => Validate("Testing", Clean(), contentRoot), Throws.Nothing, "control: all seven present");
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static void Validate(string environment, Dictionary<string, string?> settings, string? contentRoot = null)
        => NotificationConfigurationGuard.Validate(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new StubEnvironment(environment, contentRoot ?? TestContext.CurrentContext.TestDirectory));

    private sealed class StubEnvironment(string name, string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "EShop.Notification.API";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
