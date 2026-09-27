using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.SystemAdmin;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.Configuration;

/// <summary>
/// Admin panel S19, endpoint #85: Ordering's slice of the read-only settings, which the gateway composes.
///
/// <para>
/// The answer's two <c>false</c>s are literals, so their only honest guard is the behaviour they describe: an order's
/// total is the sum of its lines. <see cref="TheAnswer_DescribesHowAnOrderIsActuallyPriced"/> creates one and checks
/// both in the same test, so adding a tax or a shipping line to the total turns it red until the settings say so.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderingSettingsTests : AuthenticatedIntegrationTestBase
{
    private const string Path = "/api/v1/admin/settings";

    [Test]
    public async Task TheAnswer_DescribesHowAnOrderIsActuallyPriced()
    {
        var settings = await Client.GetFromJsonAsync<PricingSettingsDto>(Path);

        settings.Should().Be(new PricingSettingsDto("USD", TaxApplied: false, ShippingCharged: false));

        var created = await Client.PostAsJsonAsync("/api/v1/orders", new CreateOrderRequest
        {
            UserId = TestUserId,
            Street = "1 Pricing Way",
            City = "Settings",
            State = "CA",
            ZipCode = "90210",
            Country = "US",
            Items =
            [
                new() { ProductId = Factory.Catalog.Add("Priced Widget", 12.50m), Quantity = 3 },
                new() { ProductId = Factory.Catalog.Add("Priced Gadget", 7.25m), Quantity = 2 }
            ]
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = created.Headers.Location!.ToString();
        var order = await Client.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{location[(location.LastIndexOf('/') + 1)..]}");

        // No tax and no shipping: the total is the lines and nothing else.
        order!.TotalPrice.Should().Be(12.50m * 3 + 7.25m * 2);
    }

    [Test]
    public async Task TheSystemManagePermission_IsEnough_WithoutTheAdminRole()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            CreateToken(role: null, new Claim(EShopPermissions.ClaimType, EShopPermissions.SystemManage)));

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AnOrdersPermission_IsNot()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            CreateToken(role: null, new Claim(EShopPermissions.ClaimType, EShopPermissions.OrdersRead)));

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ACustomer_IsForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Customer"));

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnAnonymousCaller_IsUnauthorized()
    {
        using var anonymous = Factory.CreateClient();
        (await anonymous.GetAsync(Path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
