using System.Net;
using System.Text.Json;
using EShop.Ordering.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Api;

/// <summary>
/// Frontend-contracts F-01. Ordering sent every enum as an integer — an order's <c>status</c>, the stats'
/// <c>groupBy</c> and <c>byStatus[].status</c>, the history's <c>fromStatus</c>/<c>toStatus</c> — while its own
/// filters took names only, so a client could not send back a value it had just read. All of them are now names.
/// </summary>
/// <remarks>
/// Raw JSON on purpose: the response models also refuse an integer (<c>Models/EnumNameConverter</c>), but the test that
/// owns the claim should not depend on a model's attribute.
/// </remarks>
[TestFixture]
[Category("Integration")]
public class EnumContractTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    private async Task<Guid> SeedPaidOrderAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return (await OrderingDataHelper.CreatePaidOrderAsync(scope.ServiceProvider, TestUserId)).Id;
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await Client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    [Test]
    public async Task AnOrder_SendsItsStatusAsItsName()
    {
        var id = await SeedPaidOrderAsync();

        var order = await GetJsonAsync($"{OrdersEndpoint}/{id}");

        order.GetProperty("status").GetString().Should().Be("Paid");
    }

    [Test]
    public async Task TheList_SendsTheStatusAsItsName_AndTakesItBack()
    {
        var id = await SeedPaidOrderAsync();

        var page = await GetJsonAsync($"{OrdersEndpoint}?search={id}&status=paid");

        var item = page.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("status").GetString().Should().Be("Paid");
    }

    [Test]
    public async Task TheHistory_SendsBothStatusesAsNames()
    {
        var id = await SeedPaidOrderAsync();

        var history = (await GetJsonAsync($"{OrdersEndpoint}/{id}/history")).EnumerateArray().ToList();

        history[0].GetProperty("fromStatus").ValueKind.Should().Be(JsonValueKind.Null);
        history[0].GetProperty("toStatus").GetString().Should().Be("Pending");
        history[1].GetProperty("fromStatus").GetString().Should().Be("Pending");
        history[1].GetProperty("toStatus").GetString().Should().Be("Paid");
    }

    [Test]
    public async Task TheStats_SendGroupByAndEveryStatusAsNames()
    {
        var stats = await GetJsonAsync($"{OrdersEndpoint}/stats?groupBy=month");

        stats.GetProperty("groupBy").GetString().Should().Be("Month");
        stats.GetProperty("byStatus").EnumerateArray().Select(s => s.GetProperty("status").GetString())
            .Should().Equal("Pending", "Paid", "Shipped", "Delivered", "Cancelled", "Refunded");
    }
}
