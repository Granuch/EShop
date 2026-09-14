using EShop.Notification.Infrastructure.Services;

namespace EShop.Notification.UnitTests.Services;

/// <summary>Notification audit S7 (L21): the plain-text part derived from a rendered template.</summary>
[TestFixture]
public class PlainTextBodyTests
{
    [Test]
    public void ALink_KeepsItsTarget_Decoded()
        => Assert.That(
            PlainTextBody.FromHtml("<p><a href=\"https://shop.test/reset?userId=u1&amp;token=t1\" style=\"x\">Reset password</a></p>"),
            Is.EqualTo("Reset password (https://shop.test/reset?userId=u1&token=t1)"));

    [Test]
    public void AMailtoLink_IsJustItsAddress()
        => Assert.That(
            PlainTextBody.FromHtml("<p>Contact us at\n    <a href=\"mailto:help@eshop.local\">help@eshop.local</a>.</p>"),
            Is.EqualTo("Contact us at help@eshop.local."));

    [Test]
    public void TheHeadTagsAndEntities_AreRemoved_AndBlocksBecomeLines()
        => Assert.That(
            PlainTextBody.FromHtml(
                "<html><head><title>T</title><style>p{}</style></head><body><h1>Hi</h1><p>A &amp; B</p>"
                + "<table><tr><td>Order</td><td>42</td></tr></table></body></html>"),
            Is.EqualTo("Hi\nA & B\nOrder 42"));
}
