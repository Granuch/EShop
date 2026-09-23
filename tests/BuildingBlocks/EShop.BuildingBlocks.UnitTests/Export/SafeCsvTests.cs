using System.Globalization;
using System.Text;
using EShop.BuildingBlocks.Infrastructure.Export;

namespace EShop.BuildingBlocks.UnitTests.Export;

/// <summary>
/// Admin panel S16. The CSV field rules both exports now share — moved here from Payment's writer, whose own tests
/// still pin them through its output. These pin them at the source, so a change is caught before either writer.
/// </summary>
[TestFixture]
public class SafeCsvTests
{
    [TestCase("=HYPERLINK(\"http://evil\")")]
    [TestCase("+1+1")]
    [TestCase("-2+3")]
    [TestCase("@SUM(A1)")]
    [TestCase("\tcmd")]
    [TestCase("\rcmd")]
    public void AFieldASpreadsheetWouldRunAsAFormula_IsMadeInert(string value)
    {
        var field = SafeCsv.Field(value);

        // Inert means it no longer OPENS with the trigger character inside the quotes; quoting alone does not do that.
        Assert.That(field, Does.StartWith("\"'"));
    }

    [Test]
    public void AFieldIsQuoted_AndItsQuotesDoubled()
        => Assert.That(SafeCsv.Field("a \"b\", c"), Is.EqualTo("\"a \"\"b\"\", c\""));

    [Test]
    public void AnOrdinaryField_IsQuoted_AndOtherwiseUnchanged()
        => Assert.That(SafeCsv.Field("Blue widget"), Is.EqualTo("\"Blue widget\""));

    [TestCase(null)]
    [TestCase("")]
    public void NullAndEmpty_AreAnEmptyField(string? value) => Assert.That(SafeCsv.Field(value), Is.Empty);

    [Test]
    public void ADecimal_IsWrittenInTheInvariantCulture_WhateverTheThreadsCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.That(SafeCsv.Decimal(1234.5m), Is.EqualTo("1234.50"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void ATimestamp_IsRoundTripUtc()
        => Assert.That(
            SafeCsv.Timestamp(new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc)),
            Is.EqualTo("2026-09-23T10:00:00.0000000Z"));

    [Test]
    public void TheFile_StartsWithAUtf8ByteOrderMark()
    {
        var bytes = SafeCsv.Encode("Name\r\n\"Café\"\r\n");

        Assert.Multiple(() =>
        {
            Assert.That(bytes.Take(3), Is.EqualTo(Encoding.UTF8.GetPreamble()));
            Assert.That(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), Is.EqualTo("Name\r\n\"Café\"\r\n"));
        });
    }
}
