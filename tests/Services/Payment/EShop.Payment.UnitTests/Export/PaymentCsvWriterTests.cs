using System.Globalization;
using EShop.Payment.API.Infrastructure.Export;
using EShop.Payment.Application.Payments.Common;

namespace EShop.Payment.UnitTests.Export;

/// <summary>
/// Admin panel S10 (endpoint #69). The accounting CSV. Every case here is about a field whose contents are not under
/// our control — <c>ErrorMessage</c> carries Stripe's decline reasons and an operator's own refund note, and
/// <c>UserId</c> is whatever Identity minted.
/// </summary>
[TestFixture]
public class PaymentCsvWriterTests
{
    private static readonly DateTime Created = new(2026, 9, 20, 8, 30, 0, DateTimeKind.Utc);

    private static PaymentDto A(string? errorMessage = null, decimal amount = 1234.50m, string userId = "user-1")
        => new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            userId,
            amount,
            "USD",
            "Stripe",
            "SUCCESS",
            "pi_1",
            errorMessage,
            Created,
            Created,
            Created);

    private static string[] Lines(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Test]
    public void TheFirstLine_IsTheHeader_AndEveryRowHasThatManyFields()
    {
        var csv = PaymentCsvWriter.Write([A(), A()]);

        var lines = Lines(csv);
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Is.EqualTo(string.Join(',', PaymentCsvWriter.Columns)));
            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(lines[1].Split(',').Length, Is.EqualTo(PaymentCsvWriter.Columns.Length));
        });
    }

    [Test]
    public void AnEmptyExport_IsStillTheHeader()
    {
        var csv = PaymentCsvWriter.Write([]);

        Assert.That(Lines(csv), Is.EqualTo(new[] { string.Join(',', PaymentCsvWriter.Columns) }));
    }

    /// <summary>
    /// Without quoting, a comma inside a refund note shifts every later column of that row — silently, into a file
    /// nobody reads by eye.
    /// </summary>
    [Test]
    public void AFieldWithACommaOrAQuote_IsQuotedAndEscaped()
    {
        var csv = PaymentCsvWriter.Write([A(errorMessage: "Refunded, per \"policy\" 4")]);

        Assert.That(csv, Does.Contain("\"Refunded, per \"\"policy\"\" 4\""));
    }

    [Test]
    public void AFieldWithANewline_StaysInsideItsQuotes()
    {
        var csv = PaymentCsvWriter.Write([A(errorMessage: "line one\nline two")]);

        Assert.Multiple(() =>
        {
            Assert.That(csv, Does.Contain("\"line one\nline two\""));
            Assert.That(Lines(csv), Has.Length.EqualTo(2), "a \\n inside a quoted field is not a record separator");
        });
    }

    /// <summary>
    /// CSV injection: Excel and Sheets evaluate a field that opens with one of these as a formula, quoted or not. A
    /// refund reason is free text an operator types, so this is a reachable path, not a theoretical one.
    /// </summary>
    [TestCase("=1+1")]
    [TestCase("+1")]
    [TestCase("-1")]
    [TestCase("@SUM(A1)")]
    public void AFieldThatASpreadsheetWouldTreatAsAFormula_IsMadeInert(string dangerous)
    {
        var csv = PaymentCsvWriter.Write([A(errorMessage: dangerous)]);

        Assert.That(csv, Does.Contain("\"'" + dangerous + "\""),
            "a leading apostrophe is what stops a spreadsheet evaluating the field");
    }

    [Test]
    public void AnOrdinaryFieldIsNotPrefixed()
    {
        var csv = PaymentCsvWriter.Write([A(errorMessage: "Card declined")]);

        Assert.Multiple(() =>
        {
            Assert.That(csv, Does.Contain("\"Card declined\""));
            Assert.That(csv, Does.Not.Contain("'Card declined"));
        });
    }

    /// <summary>
    /// A machine writing "1234,56" on a comma-separated line produces a file that parses into the wrong number of
    /// columns. The formatting is pinned against a culture that would do exactly that.
    /// </summary>
    [Test]
    public void TheAmount_IsInvariantAndTwoDecimals_WhateverTheThreadCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var csv = PaymentCsvWriter.Write([A(amount: 1234.5m)]);

            Assert.That(csv, Does.Contain("\"1234.50\""));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void ATimestamp_IsRoundTripUtc()
    {
        var csv = PaymentCsvWriter.Write([A()]);

        Assert.That(csv, Does.Contain("\"2026-09-20T08:30:00.0000000Z\""));
    }

    [Test]
    public void AnAbsentFieldIsEmpty_NotTheWordNull()
    {
        var csv = PaymentCsvWriter.Write([A(errorMessage: null)]);

        Assert.Multiple(() =>
        {
            Assert.That(csv, Does.Not.Contain("null"));
            Assert.That(Lines(csv)[1], Does.Contain(",,"), "an absent field is written as nothing at all");
        });
    }
}
