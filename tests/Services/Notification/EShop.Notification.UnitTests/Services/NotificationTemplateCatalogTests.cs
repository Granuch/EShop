using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Notification.UnitTests.Services;

/// <summary>Admin panel S13 (#75/#76). The template list, and a test send of each through the real email service's seam.</summary>
[TestFixture]
public class NotificationTemplateCatalogTests
{
    private Mock<IEmailService> _email = null!;
    private NotificationTemplateCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        _email = new Mock<IEmailService>(MockBehavior.Strict);
        _catalog = new NotificationTemplateCatalog(
            _email.Object,
            Options.Create(new SmtpSettings { FromEmail = "support@eshop.test" }),
            Options.Create(new PasswordResetSettings { ResetUrlBase = "https://shop.eshop.test/reset-password" }));
    }

    /// <summary>
    /// The startup guard checks every file in <see cref="NotificationTemplates.All"/> exists; a template added there but
    /// not here would ship with no way to test it, and one listed here but not there would test a file nothing sends.
    /// </summary>
    [Test]
    public void TheCatalog_ListsExactlyTheTemplatesTheServiceSends()
    {
        Assert.That(_catalog.Templates.Select(t => t.Name), Is.EquivalentTo(NotificationTemplates.All));
    }

    [Test]
    public void OnlyThePasswordReset_IsNotResendable()
    {
        Assert.That(_catalog.Templates.Where(t => !t.Resendable).Select(t => t.Name),
            Is.EqualTo(new[] { NotificationTemplates.PasswordReset }));
    }

    /// <summary>Each template reaches its own send method — a mismatch would test-send the wrong email under the right name.</summary>
    [Test]
    public async Task EveryTemplate_TestSendsThroughItsOwnSendMethod_ToTheGivenAddress()
    {
        var recipient = new RecipientAddress("ops@eshop.test", "Ops");
        var sent = new List<string>();

        _email.Setup(e => e.SendOrderConfirmationAsync(recipient, It.Is<OrderConfirmationEmailModel>(m => m.CustomerName == "Ops"), It.IsAny<CancellationToken>()))
            .Callback(() => sent.Add(NotificationTemplates.OrderCreated)).ReturnsAsync("m1");
        _email.Setup(e => e.SendOrderShippedAsync(recipient, It.IsAny<OrderShippedEmailModel>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent.Add(NotificationTemplates.OrderShipped)).ReturnsAsync("m2");
        _email.Setup(e => e.SendPaymentCreatedAsync(recipient, It.IsAny<PaymentCreatedEmailModel>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent.Add(NotificationTemplates.PaymentCreated)).ReturnsAsync("m3");
        _email.Setup(e => e.SendPaymentCompletedAsync(recipient, It.IsAny<PaymentCompletedEmailModel>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent.Add(NotificationTemplates.PaymentCompleted)).ReturnsAsync("m4");
        _email.Setup(e => e.SendPaymentFailedAsync(recipient, It.Is<PaymentFailedEmailModel>(m => m.SupportEmail == "support@eshop.test"), It.IsAny<CancellationToken>()))
            .Callback(() => sent.Add(NotificationTemplates.PaymentFailed)).ReturnsAsync("m5");
        _email.Setup(e => e.SendPaymentRefundedAsync(recipient, It.IsAny<PaymentRefundedEmailModel>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent.Add(NotificationTemplates.PaymentRefunded)).ReturnsAsync("m6");
        _email.Setup(e => e.SendPasswordResetAsync(recipient, It.IsAny<PasswordResetEmailModel>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent.Add(NotificationTemplates.PasswordReset)).ReturnsAsync("m7");

        foreach (var template in NotificationTemplates.All)
        {
            await _catalog.SendTestAsync(template, recipient);
        }

        Assert.That(sent, Is.EqualTo(NotificationTemplates.All));
    }

    /// <summary>The sample reset link points at the real reset page with a token no account holds: following it resets nothing.</summary>
    [Test]
    public async Task ThePasswordResetSample_LinksToTheRealPage_WithATokenThatResetsNothing()
    {
        PasswordResetEmailModel? model = null;
        _email.Setup(e => e.SendPasswordResetAsync(It.IsAny<RecipientAddress>(), It.IsAny<PasswordResetEmailModel>(), It.IsAny<CancellationToken>()))
            .Callback<RecipientAddress, PasswordResetEmailModel, CancellationToken>((_, m, _) => model = m)
            .ReturnsAsync("m");

        await _catalog.SendTestAsync(NotificationTemplates.PasswordReset, new RecipientAddress("ops@eshop.test"));

        Assert.Multiple(() =>
        {
            Assert.That(model!.ResetLink, Does.StartWith("https://shop.eshop.test/reset-password?"));
            Assert.That(model.ResetLink, Does.Contain("token=not-a-real-token"));
            Assert.That(model.CustomerName, Is.EqualTo("there"), "no name given: the greeting a nameless customer gets");
        });
    }

    [Test]
    public void AnUnknownTemplate_IsRefused()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _catalog.SendTestAsync("no-such-template", new RecipientAddress("ops@eshop.test")));
    }
}
