using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Notification.UnitTests.Services;

[TestFixture]
public class EmailServiceTests
{
    private static EmailService CreateService(Mock<ITemplateRenderer> renderer)
    {
        var settings = Options.Create(new SmtpSettings
        {
            Host = "smtp.mailtrap.io",
            Port = 587,
            Username = "user",
            Password = "password",
            UseSsl = true,
            FromEmail = "noreply@eshop.local",
            FromName = "EShop"
        });

        return new EmailService(settings, renderer.Object, Mock.Of<ILogger<EmailService>>());
    }

    [Test]
    public void SendOrderConfirmationAsync_ShouldRenderExpectedTokens()
    {
        var renderer = new Mock<ITemplateRenderer>();
        renderer
            .Setup(x => x.RenderAsync("order-created", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stop-after-render"));

        var service = CreateService(renderer);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.SendOrderConfirmationAsync(
            new RecipientAddress("customer@test.com", "Customer"),
            new OrderConfirmationEmailModel
            {
                OrderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                CustomerName = "Customer",
                OrderDate = new DateTimeOffset(2026, 01, 01, 12, 00, 00, TimeSpan.Zero),
                TotalAmount = 150.75m,
                ItemCount = 3
            }));

        renderer.Verify(x => x.RenderAsync(
            "order-created",
            It.Is<IReadOnlyDictionary<string, string>>(d =>
                d["OrderId"] == "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                && d["CustomerName"] == "Customer"
                && d["TotalAmount"] == "150.75"
                && d["ItemCount"] == "3"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void SendOrderShippedAsync_ShouldRenderExpectedTokens()
    {
        var renderer = new Mock<ITemplateRenderer>();
        renderer
            .Setup(x => x.RenderAsync("order-shipped", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stop-after-render"));

        var service = CreateService(renderer);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.SendOrderShippedAsync(
            new RecipientAddress("customer@test.com", "Customer"),
            new OrderShippedEmailModel
            {
                OrderId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CustomerName = "Customer",
                TrackingNumber = "TRK-555",
                EstimatedDelivery = "2026-01-10"
            }));

        renderer.Verify(x => x.RenderAsync(
            "order-shipped",
            It.Is<IReadOnlyDictionary<string, string>>(d =>
                d["OrderId"] == "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
                && d["CustomerName"] == "Customer"
                && d["TrackingNumber"] == "TRK-555"
                && d["EstimatedDelivery"] == "2026-01-10"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void SendPaymentFailedAsync_ShouldRenderExpectedTokens()
    {
        var renderer = new Mock<ITemplateRenderer>();
        renderer
            .Setup(x => x.RenderAsync("payment-failed", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stop-after-render"));

        var service = CreateService(renderer);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.SendPaymentFailedAsync(
            new RecipientAddress("customer@test.com", "Customer"),
            new PaymentFailedEmailModel
            {
                OrderId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                CustomerName = "Customer",
                Amount = 88.40m,
                FailureReason = "Card declined",
                SupportEmail = "support@eshop.local"
            }));

        renderer.Verify(x => x.RenderAsync(
            "payment-failed",
            It.Is<IReadOnlyDictionary<string, string>>(d =>
                d["OrderId"] == "cccccccc-cccc-cccc-cccc-cccccccccccc"
                && d["CustomerName"] == "Customer"
                && d["Amount"] == "88.40"
                && d["FailureReason"] == "Card declined"
                && d["SupportEmail"] == "support@eshop.local"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
