using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Integration tests for GET /api/v1/users/{userId}/orders endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class GetOrdersByUserTests : AuthenticatedIntegrationTestBase
{
    [Test]
    public async Task GetOrdersByUser_WithExistingOrders_ShouldReturnOk()
    {
        // Arrange
        using var scope = CreateScope();
        await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId: "user-with-orders");
        await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId: "user-with-orders");

        // Act
        var response = await Client.GetAsync("/api/v1/users/user-with-orders/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedOrderResponse>();
        result.Should().NotBeNull();
        result!.Items.Count.Should().BeGreaterThanOrEqualTo(2);
        result.Items.Should().OnlyContain(o => o.UserId == "user-with-orders");
    }

    [Test]
    public async Task GetOrdersByUser_WithNoOrders_ShouldReturnEmptyList()
    {
        // Act
        var response = await Client.GetAsync($"/api/v1/users/non-existent-user-{Guid.NewGuid()}/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedOrderResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Audit M4. Cursor mode skipped orders sharing the boundary timestamp and reported offset-shaped
    /// paging, so it was removed. Removed means rejected: an ignored parameter would hand a client
    /// paging by cursor page one, with a 200, forever. Both a timestamp (the old shape) and an opaque
    /// token (anything else) must reach the validator rather than fail binding with a JSON message.
    /// </summary>
    [TestCase("2026-01-01T00:00:00Z")]
    [TestCase("opaque-token")]
    public async Task ACursor_IsRejected_RatherThanIgnored(string cursor)
    {
        var response = await Client.GetAsync(
            $"/api/v1/users/{TestUserId}/orders?cursor={Uri.EscapeDataString(cursor)}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("Validation.Failed").And.Contain("Cursor");
    }

    [Test]
    public async Task OffsetPaging_ReturnsEveryOrderExactlyOnce_WithHonestPageCounts()
    {
        var userId = $"pager-{Guid.NewGuid():N}";
        var created = new List<Guid>();
        using (var scope = CreateScope())
        {
            for (var i = 0; i < 5; i++)
            {
                created.Add((await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId: userId)).Id);
            }
        }

        var pages = new List<PagedOrderResponse>();
        for (var page = 1; page <= 3; page++)
        {
            pages.Add((await Client.GetFromJsonAsync<PagedOrderResponse>(
                $"/api/v1/users/{userId}/orders?pageNumber={page}&pageSize=2"))!);
        }

        pages.SelectMany(p => p.Items).Select(o => o.Id).Should().BeEquivalentTo(created);
        pages[0].TotalCount.Should().Be(5);
        pages[0].TotalPages.Should().Be(3);
        pages[0].HasNextPage.Should().BeTrue();
        pages[2].Items.Should().ContainSingle();
        pages[2].HasNextPage.Should().BeFalse();
    }

    /// <summary>
    /// Ordering audit M7/M11. <c>CreatedAt</c> alone is not unique, so the lists order by
    /// <c>(CreatedAt, Id)</c>. On InMemory a missing tie-break could not be seen, because InMemory sorts
    /// stably. Postgres, given only <c>CreatedAt</c>, returns ties in physical order, which matches
    /// Id-descending by chance only once in 120 for five rows. Expected order compares ids as strings,
    /// because Postgres orders uuids by their bytes in textual order.
    /// </summary>
    [Test]
    public async Task OrdersCreatedAtTheSameInstant_ComeBackNewestIdFirst_EachExactlyOnce()
    {
        var userId = $"ties-{Guid.NewGuid():N}";
        var created = new List<Guid>();
        using (var scope = CreateScope())
        {
            for (var i = 0; i < 5; i++)
            {
                created.Add((await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId: userId)).Id);
            }

            var instant = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Orders\" SET \"CreatedAt\" = {instant} WHERE \"UserId\" = {userId}");
        }

        var returned = new List<Guid>();
        for (var page = 1; page <= 5; page++)
        {
            returned.AddRange((await Client.GetFromJsonAsync<PagedOrderResponse>(
                $"/api/v1/users/{userId}/orders?pageNumber={page}&pageSize=1"))!.Items.Select(o => o.Id));
        }

        returned.Should().Equal(created.OrderByDescending(id => id.ToString(), StringComparer.Ordinal));
    }

    [Test]
    public async Task GetOrdersByUser_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await Client.GetAsync("/api/v1/users/some-user/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
