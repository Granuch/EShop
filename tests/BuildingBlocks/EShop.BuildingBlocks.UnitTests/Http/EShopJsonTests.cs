using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.BuildingBlocks.UnitTests.Http;

/// <summary>
/// Frontend-contracts F-01. System.Text.Json writes an enum as a number unless told otherwise, which is how Catalog and
/// Ordering came to send <c>"status": 1</c> while the other services sent names.
/// </summary>
[TestFixture]
public class EShopJsonTests
{
    private enum Probe
    {
        First,
        SecondValue
    }

    private sealed record Holder(Probe Value, Probe? Maybe);

    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions(JsonSerializerDefaults.Web).UseEShopConventions();

    [Test]
    public void AnEnum_IsWrittenAsItsPascalCaseName()
    {
        Assert.That(JsonSerializer.Serialize(new Holder(Probe.SecondValue, null), Options),
            Is.EqualTo("{\"value\":\"SecondValue\",\"maybe\":null}"));
    }

    [TestCase("\"SecondValue\"")]
    [TestCase("\"secondvalue\"")]
    [TestCase("\"SECONDVALUE\"")]
    public void AName_IsReadInAnyCase(string json)
    {
        Assert.That(JsonSerializer.Deserialize<Probe>(json, Options), Is.EqualTo(Probe.SecondValue));
    }

    /// <summary>A number would bind as a value no row can have, the same reason the query validators refuse one.</summary>
    [TestCase("1")]
    [TestCase("\"1\"")]
    [TestCase("7")]
    [TestCase("\"Bogus\"")]
    public void ANumberOrAnUnknownName_IsRefused(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Probe>(json, Options));
    }

    [Test]
    public void AddEShopJson_ConfiguresTheMinimalApiOptions()
    {
        using var provider = new ServiceCollection().AddEShopJson().BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;

        Assert.That(JsonSerializer.Serialize(Probe.First, options.SerializerOptions), Is.EqualTo("\"First\""));
    }

    [Test]
    public void AddEShopJson_ConfiguresTheMvcOptions()
    {
        var services = new ServiceCollection();
        services.AddControllers().AddEShopJson();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value;

        Assert.That(JsonSerializer.Serialize(Probe.First, options.JsonSerializerOptions), Is.EqualTo("\"First\""));
    }
}
