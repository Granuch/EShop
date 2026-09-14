using System.Text.RegularExpressions;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Notification.UnitTests.Services;

/// <summary>
/// What each email says: the tokens <c>EmailService</c> supplies, and the templates they fill. The renderer is mocked
/// and stops the send after rendering; <c>EmailServiceSmtpTests</c> covers the SMTP half.
/// </summary>
[TestFixture]
public class EmailServiceTests
{
    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);
    private static readonly RecipientAddress Customer = new("customer@test.com", "Customer");

    /// <summary>
    /// Notification audit S7 (L23, L25). Every template is filled by exactly the tokens its sender supplies: a placeholder
    /// with no token would ship as literal <c>{{X}}</c>, and a token with no placeholder is a field the email silently drops.
    /// </summary>
    [Test]
    public async Task EveryTemplate_UsesExactlyTheTokensItsSenderSupplies()
    {
        var tokensPerTemplate = await TokensPerTemplateAsync();
        var root = TemplateRenderer.ResolveTemplatesRoot(TestContext.CurrentContext.TestDirectory);

        Assert.That(tokensPerTemplate.Keys, Is.EquivalentTo(NotificationTemplates.All), "every template has a sender");
        Assert.Multiple(() =>
        {
            foreach (var (template, tokens) in tokensPerTemplate)
            {
                var placeholders = Placeholder.Matches(File.ReadAllText(Path.Combine(root, $"{template}.html")))
                    .Select(match => match.Groups[1].Value)
                    .ToHashSet();

                Assert.That(placeholders, Is.EquivalentTo(tokens.Keys), template);
            }
        });
    }

    /// <summary>S7 (M12, D10): Ordering cancels the order, so the email cannot ask the customer to retry it.</summary>
    [Test]
    public void ThePaymentFailedEmail_SaysTheOrderWasCancelled_AndNothingWasCharged()
    {
        var template = Template(NotificationTemplates.PaymentFailed);

        Assert.Multiple(() =>
        {
            Assert.That(template, Does.Contain("Your order was cancelled"));
            Assert.That(template, Does.Contain("Nothing was charged"));
            Assert.That(template, Does.Contain("You can place a new order at any time"));
            Assert.That(template, Does.Not.Contain("try again"));
        });
    }

    /// <summary>S7 (L18): no estimated delivery date — nothing supplies one — and no "track your package" promise.</summary>
    [Test]
    public void TheShippedEmail_PromisesNoDeliveryDate()
    {
        var template = Template(NotificationTemplates.OrderShipped);

        Assert.Multiple(() =>
        {
            Assert.That(template, Does.Not.Contain("Estimated").IgnoreCase);
            Assert.That(template, Does.Not.Contain("Track your package"));
            Assert.That(template, Does.Contain("{{ShippedAt}}"));
        });
    }

    /// <summary>S7 (D11): the total shows its currency; the template used to print a dollar sign.</summary>
    [Test]
    public void TheOrderConfirmation_ShowsTheCurrency_NotADollarSign()
    {
        var template = Template(NotificationTemplates.OrderCreated);

        Assert.Multiple(() =>
        {
            Assert.That(template, Does.Not.Contain("${{"));
            Assert.That(template, Does.Contain("{{TotalAmount}} {{Currency}}"));
        });
    }

    /// <summary>S7 (L16): the greeting is in the body ("Hi there,"), never in a heading ("Thanks for your order, there!").</summary>
    [Test]
    public void NoTemplate_PutsTheCustomersNameInAHeading()
    {
        Assert.Multiple(() =>
        {
            foreach (var template in NotificationTemplates.All)
            {
                Assert.That(Regex.IsMatch(Template(template), @"<h1[^>]*>[^<]*\{\{CustomerName\}\}"), Is.False, template);
            }
        });
    }

    [Test]
    public async Task TheOrderConfirmation_Tokens()
    {
        var tokens = (await TokensPerTemplateAsync())[NotificationTemplates.OrderCreated];

        Assert.Multiple(() =>
        {
            Assert.That(tokens["OrderId"], Is.EqualTo("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            Assert.That(tokens["TotalAmount"], Is.EqualTo("150.75"));
            Assert.That(tokens["Currency"], Is.EqualTo("USD"));
            Assert.That(tokens["ItemCount"], Is.EqualTo("3"));
        });
    }

    [Test]
    public async Task TheShippedEmail_Tokens_WithoutATrackingNumber()
    {
        var tokens = (await TokensPerTemplateAsync())[NotificationTemplates.OrderShipped];

        Assert.Multiple(() =>
        {
            Assert.That(tokens["ShippedAt"], Is.EqualTo("2026-01-05"));
            Assert.That(tokens["TrackingNumber"], Is.EqualTo("Not available yet"));
        });
    }

    [Test]
    public async Task ThePaymentFailedAndPasswordResetEmails_Tokens()
    {
        var tokensPerTemplate = await TokensPerTemplateAsync();
        var failed = tokensPerTemplate[NotificationTemplates.PaymentFailed];
        var reset = tokensPerTemplate[NotificationTemplates.PasswordReset];

        Assert.Multiple(() =>
        {
            Assert.That(failed["FailureReason"], Is.EqualTo("Your card was declined."));
            Assert.That(failed["SupportEmail"], Is.EqualTo("support@eshop.local"));
            Assert.That(reset["ResetLink"], Is.EqualTo("https://frontend/reset-password?userId=u1&token=t1"));
        });
    }

    /// <summary>Calls every sender once with a renderer that records its tokens and then stops the send.</summary>
    private static async Task<Dictionary<string, IReadOnlyDictionary<string, string>>> TokensPerTemplateAsync()
    {
        var captured = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var renderer = new Mock<ITemplateRenderer>();
        renderer
            .Setup(x => x.RenderAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyDictionary<string, string>, CancellationToken>((name, tokens, _) => captured[name] = tokens)
            .ThrowsAsync(new OperationCanceledException("stop after rendering"));

        var service = new EmailService(
            Options.Create(new SmtpSettings { Host = "smtp.invalid", FromEmail = "noreply@eshop.local", FromName = "EShop" }),
            renderer.Object,
            Mock.Of<ILogger<EmailService>>());
        var orderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await StopAsync(() => service.SendOrderConfirmationAsync(Customer, new OrderConfirmationEmailModel
        {
            OrderId = orderId, CustomerName = "Customer", OrderDate = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            TotalAmount = 150.75m, Currency = "USD", ItemCount = 3
        }));
        await StopAsync(() => service.SendOrderShippedAsync(Customer, new OrderShippedEmailModel
        {
            OrderId = orderId, CustomerName = "Customer", TrackingNumber = null, ShippedAt = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc)
        }));
        await StopAsync(() => service.SendPaymentCreatedAsync(Customer, new PaymentCreatedEmailModel
        {
            OrderId = orderId, CustomerName = "Customer", Amount = 10m, Currency = "USD", CreatedAt = DateTime.UtcNow
        }));
        await StopAsync(() => service.SendPaymentCompletedAsync(Customer, new PaymentCompletedEmailModel
        {
            OrderId = orderId, CustomerName = "Customer", Amount = 10m, Currency = "USD", CompletedAt = DateTime.UtcNow
        }));
        await StopAsync(() => service.SendPaymentFailedAsync(Customer, new PaymentFailedEmailModel
        {
            OrderId = orderId, CustomerName = "Customer", FailureReason = "Your card was declined.", SupportEmail = "support@eshop.local"
        }));
        await StopAsync(() => service.SendPaymentRefundedAsync(Customer, new PaymentRefundedEmailModel
        {
            OrderId = orderId, CustomerName = "Customer", Amount = 10m, Currency = "USD", RefundedAt = DateTime.UtcNow,
            SupportEmail = "support@eshop.local"
        }));
        await StopAsync(() => service.SendPasswordResetAsync(Customer, new PasswordResetEmailModel
        {
            CustomerName = "Customer", ResetLink = "https://frontend/reset-password?userId=u1&token=t1"
        }));

        return captured;
    }

    private static async Task StopAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (OperationCanceledException)
        {
            // The renderer stops every send after recording its tokens.
        }
    }

    private static string Template(string name)
        => File.ReadAllText(Path.Combine(
            TemplateRenderer.ResolveTemplatesRoot(TestContext.CurrentContext.TestDirectory), $"{name}.html"));
}
