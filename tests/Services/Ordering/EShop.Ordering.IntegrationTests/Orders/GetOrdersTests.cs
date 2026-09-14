using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// GET /api/v1/orders (admin). Audit M3: it had no validator, so a bad page or page size went straight
/// to the query and an unknown status was dropped — <c>?status=Payed</c> answered with every order.
/// </summary>
[TestFixture]
[Category("Integration")]
public class GetOrdersTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    [TestCase("pageNumber=0", "PageNumber")]
    [TestCase("pageSize=0", "PageSize")]
    [TestCase("pageSize=101", "PageSize")]
    [TestCase("status=Payed", "Status")]
    [TestCase("status=7", "Status")]
    public async Task AnInvalidQuery_IsRejected_NamingTheParameter(string queryString, string parameter)
    {
        var response = await Client.GetAsync($"{OrdersEndpoint}?{queryString}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("Validation.Failed").And.Contain(parameter);
    }

    [Test]
    public async Task AStatusFilter_ReturnsOnlyOrdersInThatStatus_WhateverItsCase()
    {
        Guid paidId;
        using (var scope = CreateScope())
        {
            paidId = (await OrderingDataHelper.CreatePaidOrderAsync(scope.ServiceProvider, TestUserId)).Id;
        }

        var pending = await Client.PostAsJsonAsync(OrdersEndpoint, new CreateOrderRequest
        {
            UserId = TestUserId,
            Street = "1 Filter St",
            City = "Sortville",
            State = "CA",
            ZipCode = "90210",
            Country = "US",
            Items = [new CreateOrderItemRequest { ProductId = Factory.Catalog.Add("Pending Widget", 3m), Quantity = 1 }]
        });
        pending.StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await Client.GetFromJsonAsync<PagedOrderResponse>($"{OrdersEndpoint}?status=paid&pageSize=100");

        page!.Items.Should().Contain(o => o.Id == paidId);
        page.Items.Should().OnlyContain(o => o.Status == OrderStatus.Paid);
    }
}
