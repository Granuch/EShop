using System.Collections.Concurrent;
using System.Text;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;
using EShop.Notification.UnitTests.TestDoubles;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;

namespace EShop.Notification.UnitTests.Services;

/// <summary>
/// <c>EmailService</c> through a real MailKit send, against <see cref="FakeSmtpServer"/> on loopback. The older
/// <c>EmailServiceTests</c> stop at a renderer that throws, so the SMTP half had never run in a test.
/// </summary>
[TestFixture]
public class EmailServiceSmtpTests
{
    /// <summary>Notification audit S5 (M7): the address used to be logged for every email.</summary>
    [Test]
    public async Task ASentEmail_ReachesTheServer_AndTheRecipientsAddressIsNotLogged()
    {
        await using var server = FakeSmtpServer.Start();
        var logs = new ListLogger<EmailService>();

        await Service(server.Port, logs).SendPasswordResetAsync(
            new RecipientAddress("customer@test.com", "Ada Customer"),
            new PasswordResetEmailModel { CustomerName = "Ada Customer", ResetLink = "https://shop.test/reset?t=1" });

        Assert.Multiple(() =>
        {
            Assert.That(server.Recipients, Is.EqualTo(new[] { "customer@test.com" }), "precondition: the email was sent");
            Assert.That(logs.Entries, Has.Some.Contains("Email sent"));
            Assert.That(logs.Entries, Has.None.Contains("customer@test.com"));
            Assert.That(logs.Entries, Has.None.Contains("Ada Customer"));
        });
    }

    /// <summary>
    /// Notification audit S2 (H3, D2): once the server has accepted the message, a connection that dies on QUIT must not
    /// turn the send into a failure, or the consumer records it Failed and the redelivery sends it again.
    /// <para>What this pins is MailKit's behaviour, not <c>EmailService</c>'s own catch: S5 removed that catch and this
    /// test stayed green, because <c>SmtpClient.DisconnectAsync(quit: true)</c> already ignores a failed QUIT. D2 relies
    /// on that; a MailKit upgrade that changed it would turn this red.</para>
    /// </summary>
    [Test]
    public async Task AServerThatDropsTheConnectionOnQuit_DoesNotFailAnAcceptedSend()
    {
        await using var server = FakeSmtpServer.Start(dropOnQuit: true);

        Assert.DoesNotThrowAsync(() => Service(server.Port, new ListLogger<EmailService>()).SendPasswordResetAsync(
            new RecipientAddress("customer@test.com"),
            new PasswordResetEmailModel { CustomerName = "Customer", ResetLink = "https://shop.test/reset?t=1" }));
        Assert.That(server.Recipients, Has.Count.EqualTo(1));
    }

    [TestCase(SmtpSecurity.None, SecureSocketOptions.None)]
    [TestCase(SmtpSecurity.StartTls, SecureSocketOptions.StartTls)]
    [TestCase(SmtpSecurity.SslOnConnect, SecureSocketOptions.SslOnConnect)]
    public void EachSecurityMode_MapsToItsMailKitOption(SmtpSecurity security, SecureSocketOptions expected)
        => Assert.That(security.ToSocketOptions(), Is.EqualTo(expected));

    /// <summary>S5 (D7): a configuration that still sets only the old UseSsl keeps its meaning.</summary>
    [TestCase(true, null, SmtpSecurity.StartTls)]
    [TestCase(false, null, SmtpSecurity.None)]
    [TestCase(false, SmtpSecurity.SslOnConnect, SmtpSecurity.SslOnConnect)]
    [TestCase(true, SmtpSecurity.None, SmtpSecurity.None)]
    public void TheEffectiveSecurity_IsSecurity_ElseTheLegacyUseSsl(bool useSsl, SmtpSecurity? security, SmtpSecurity expected)
        => Assert.That(new SmtpSettings { UseSsl = useSsl, Security = security }.EffectiveSecurity, Is.EqualTo(expected));

    /// <summary>
    /// Notification audit S7 (L21): the email carries a plain-text part beside the HTML, with the reset link's target
    /// readable, and the id the sender returns is the message's own Message-ID.
    /// </summary>
    [Test]
    public async Task TheEmail_HasAPlainTextPartWithTheLink_AndTheReturnedIdIsItsMessageId()
    {
        await using var server = FakeSmtpServer.Start();
        const string html =
            "<p>Hi there,</p><p><a href=\"https://shop.test/reset?userId=u1&amp;token=t1\">Reset password</a></p>";

        var messageId = await Service(server.Port, new ListLogger<EmailService>(), html).SendPasswordResetAsync(
            new RecipientAddress("customer@test.com"),
            new PasswordResetEmailModel { CustomerName = "there", ResetLink = "https://shop.test/reset?userId=u1&token=t1" });

        var sent = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(server.Messages.Single())));
        Assert.Multiple(() =>
        {
            Assert.That(sent.MessageId, Is.EqualTo(messageId));
            Assert.That(sent.HtmlBody, Does.Contain("<a href="));
            Assert.That(sent.TextBody, Does.Contain("Reset password (https://shop.test/reset?userId=u1&token=t1)"));
            Assert.That(sent.TextBody, Does.Not.Contain("<"));
        });
    }

    private static EmailService Service(int port, ILogger<EmailService> logger, string html = "<p>Hello</p>")
    {
        var renderer = new Mock<ITemplateRenderer>();
        renderer.Setup(x => x.RenderAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(html);

        var settings = Options.Create(new SmtpSettings
        {
            Host = "127.0.0.1",
            Port = port,
            Security = SmtpSecurity.None,
            FromEmail = "noreply@eshop.local",
            FromName = "EShop",
            CheckCertificateRevocation = false
        });

        return new EmailService(settings, renderer.Object, logger);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join("; ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : string.Empty;
            Entries.Enqueue($"{formatter(state, exception)} | {values}");
        }
    }
}
